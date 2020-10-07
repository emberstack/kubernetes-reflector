using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Queuing;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Secrets
{
    public class FreeNasMirror : IHostedService, IHealthCheck
    {
        private const string CertificateUri = "certificate/";

        private readonly IKubernetes _apiClient;
        private readonly IHttpClientFactory _clientFactory;

        private readonly FeederQueue<WatcherEvent<V1Secret>> _eventQueue;
        private readonly ILogger<FreeNasMirror> _logger;
        private readonly ManagedWatcher<V1Secret, V1SecretList> _secretsWatcher;

        public FreeNasMirror(ILogger<FreeNasMirror> logger,
            ManagedWatcher<V1Secret, V1SecretList> secretsWatcher,
            IKubernetes apiClient,
            IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _secretsWatcher = secretsWatcher;
            _apiClient = apiClient;
            _clientFactory = clientFactory;

            _eventQueue = new FeederQueue<WatcherEvent<V1Secret>>(OnEvent, OnEventHandlingError);

            _secretsWatcher.OnStateChanged = OnWatcherStateChanged;
            _secretsWatcher.EventHandlerFactory = e => _eventQueue.FeedAsync(e);
            _secretsWatcher.RequestFactory = async c =>
                await c.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true,
                    timeoutSeconds: Requests.WatcherTimeout);
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(_secretsWatcher.IsFaulted
                ? HealthCheckResult.Unhealthy()
                : HealthCheckResult.Healthy());
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _secretsWatcher.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _secretsWatcher.Stop();
        }

        private async Task OnWatcherStateChanged<TS, TSl>(ManagedWatcher<TS, TSl, WatcherEvent<TS>> sender,
            ManagedWatcherStateUpdate update) where TS : class, IKubernetesObject
        {
            var tag = sender.Tag ?? string.Empty;
            switch (update.State)
            {
                case ManagedWatcherState.Closed:
                    _logger.LogDebug("{type} watcher {tag} {state}", typeof(TS).Name, tag, update.State);
                    await _secretsWatcher.Stop();

                    await _eventQueue.WaitAndClear();

                    await _secretsWatcher.Start();
                    break;
                case ManagedWatcherState.Faulted:
                    _logger.LogError(update.Exception, "{type} watcher {tag} {state}", typeof(TS).Name, tag,
                        update.State);
                    break;
                case ManagedWatcherState.Stopped:
                    break;
                case ManagedWatcherState.Stopping:
                    break;
                case ManagedWatcherState.Started:
                    break;
                case ManagedWatcherState.Starting:
                    break;
                default:
                    _logger.LogDebug("{type} watcher {tag} {state}", typeof(TS).Name, tag, update.State);
                    break;
            }
        }

        private async Task OnEvent(WatcherEvent<V1Secret> e)
        {
            if (e.Item.Type.StartsWith("helm.sh")) return;

            var secretId = KubernetesObjectId.For(e.Item.Metadata());
            var item = e.Item;

            _logger.LogTrace("[{eventType}] {kind} {@id}", e.Type, e.Item.Kind, secretId);

            if (e.Type != WatchEventType.Added && e.Type != WatchEventType.Modified) return;
            if (!e.Item.Metadata.ReflectionAllowed() || !e.Item.Metadata.FreeNasReflectionEnabled()) return;
            if (!e.Item.Type.Equals("kubernetes.io/tls", StringComparison.InvariantCultureIgnoreCase)) return;

            _logger.LogDebug("FreeNas enabled using host secret {secretId}.", secretId);

            var tlsCrt = Encoding.Default.GetString(item.Data["tls.crt"]);
            var tlsKey = Encoding.Default.GetString(item.Data["tls.key"]);

            var hostSecretIds =
                item.Metadata.FreeNasReflectionHosts()
                    .Select(s => new KubernetesObjectId(s))
                    .ToList();
            var certName = item.Metadata.FreeNasCertificate();

            foreach (var hostSecretId in hostSecretIds)
            {
                _logger.LogDebug(
                    "Reflecting {secretId} to FreeNas device using host secret {hostSecretId}.",
                    secretId, hostSecretId, hostSecretId);
                string hostAddress;
                string username;
                string password;
                try
                {
                    var hostSecret = await _apiClient.ReadNamespacedSecretAsync(hostSecretId.Name,
                        string.IsNullOrWhiteSpace(hostSecretId.Namespace)
                            ? e.Item.Metadata.NamespaceProperty
                            : hostSecretId.Namespace);
                    if (hostSecret.Data is null || !hostSecret.Data.Keys.Any())
                    {
                        _logger.LogWarning("Cannot reflect {secretId} to {hostSecretId}. " +
                                           "Host secret {hostSecretId} has no data.",
                            secretId, hostSecretId, hostSecretId);
                        continue;
                    }

                    hostAddress = hostSecret.Data.ContainsKey("host")
                        ? Encoding.Default.GetString(hostSecret.Data["host"])
                        : null;
                    username = hostSecret.Data.ContainsKey("username")
                        ? Encoding.Default.GetString(hostSecret.Data["username"])
                        : null;
                    password = hostSecret.Data.ContainsKey("password")
                        ? Encoding.Default.GetString(hostSecret.Data["password"])
                        : null;
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "Cannot reflect {secretId} to {hostSecretId}. Host secret {hostSecretId} not found.",
                        secretId, hostSecretId, hostSecretId);

                    continue;
                }

                if (string.IsNullOrWhiteSpace(hostAddress) || string.IsNullOrWhiteSpace(username) ||
                    string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogWarning(
                        "Cannot reflect {secretId} to FreeNas device using host secret {hostSecretId}. " +
                        "Host secret {hostSecretId} must contain 'host', 'username' and 'password' values.",
                        secretId, hostSecretId, hostSecretId);
                    continue;
                }

                var hostParts = hostAddress.Split(new[] {":"}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (!hostParts.Any() || hostParts.Count > 2)
                    _logger.LogWarning(
                        "Cannot reflect {secretId} to FreeNas device using host secret {hostSecretId}. " +
                        "Host secret {hostSecretId} contains invalid 'host' data. " +
                        "'host' must be in the format 'host:port' where port is optional.",
                        secretId, hostSecretId, hostSecretId);

                var host = hostParts.First();

                // Check if certificate is the same
                var client = _clientFactory.CreateClient();
                client.BaseAddress = new Uri($"https://{host}/api/v2.0/");
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.ConnectionClose = true;
                client.DefaultRequestHeaders
                    .Accept
                    .Add(new MediaTypeWithQualityHeaderValue("application/json")); //ACCEPT header
                client.DefaultRequestHeaders.Add("User-Agent", "Emberstack/Reflector");

                var authenticationString = $"{username}:{password}";
                var base64EncodedAuthenticationString =
                    Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

                var response = await client.GetAsync(CertificateUri);
                var options = new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase};

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                using var responseStr = response.Content.ReadAsStringAsync();
                var certificates =
                    await JsonSerializer.DeserializeAsync<List<FreeNasCertificate>>(responseStream, options);

                // var bytes = Encoding.ASCII.GetBytes(tlsCrt);
                // var x509Certificate2 = new X509Certificate2(bytes);
                //
                // var name = $"{certName}-{x509Certificate2.NotAfter:s}";

                var name = certName;
                var cert = certificates
                    .SingleOrDefault(x => x.Name == name);
                var certExists = !(cert is null);
                if (certExists)
                    if (tlsCrt.Contains(cert.Certificate))
                    {
                        _logger.LogDebug(
                            "Skip reflecting {secretId} to FreeNas device using host secret {hostSecretId}. Already exists.",
                            secretId, hostSecretId);
                        return;
                    }

                // Create the certificate
                var bodyCreate = JsonSerializer.Serialize(new FreeNasCertificateCreateImported
                {
                    Name = name,
                    Certificate = tlsCrt,
                    Privatekey = tlsKey
                }, options);
                await client.PostAsync(CertificateUri, new StringContent(bodyCreate));

                // Set the certificate as default
                var certId = certExists
                    ? cert.Id
                    : certificates.Single(x => x.Name == name).Id;

                var bodyGeneral = JsonSerializer.Serialize(new FreeNasSystemGeneral
                    {UiCertificate = certId}, options);
                await client.PutAsync("system/general/", new StringContent(bodyGeneral));

                _logger.LogInformation("Reflected {secretId} to FreeNas device using host secret {hostSecretId}.",
                    secretId, hostSecretId);
            }
        }

        private async Task OnEventHandlingError(WatcherEvent<V1Secret> e, Exception ex)
        {
            var id = KubernetesObjectId.For(e.Item.Metadata());
            _logger.LogError(ex, "Failed to process {eventType} {kind} {@id} due to exception",
                e.Type, e.Item.Kind, id);
            await _secretsWatcher.Stop();
            _eventQueue.Clear();

            _logger.LogTrace("Watchers restarting");
            await _secretsWatcher.Start();
            _logger.LogTrace("Watchers restarted");
        }
    }

    internal class FreeNasSystemGeneral
    {
        public int UiCertificate { get; set; }
    }

    internal class FreeNasCertificateCreateImported
    {
        public string CreateType => "CERTIFICATE_CREATE_IMPORTED";
        public string Name { get; set; }
        public string Certificate { get; set; }
        public string Privatekey { get; set; }
    }

    internal class FreeNasCertificate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Certificate { get; set; }
        public string Privatekey { get; set; }
    }
}