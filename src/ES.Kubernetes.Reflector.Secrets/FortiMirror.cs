using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
using Renci.SshNet;

namespace ES.Kubernetes.Reflector.Secrets
{
    public class FortiMirror : IHostedService, IHealthCheck
    {

        private readonly FeederQueue<WatcherEvent<V1Secret>> _eventQueue;
        private readonly ILogger<FortiMirror> _logger;
        private readonly ManagedWatcher<V1Secret, V1SecretList> _secretsWatcher;
        private readonly IKubernetes _apiClient;

        public FortiMirror(ILogger<FortiMirror> logger,
            ManagedWatcher<V1Secret, V1SecretList> secretsWatcher,
            IKubernetes apiClient)
        {
            _logger = logger;
            _secretsWatcher = secretsWatcher;
            _apiClient = apiClient;

            _eventQueue = new FeederQueue<WatcherEvent<V1Secret>>(OnEvent, OnEventHandlingError);


            _secretsWatcher.OnStateChanged = OnWatcherStateChanged;
            _secretsWatcher.EventHandlerFactory = e => _eventQueue.FeedAsync(e);
            _secretsWatcher.RequestFactory = async c =>
                await c.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true);



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

        private async Task OnWatcherStateChanged<TS, TSL>(ManagedWatcher<TS, TSL, WatcherEvent<TS>> sender,
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
                default:
                    _logger.LogDebug("{type} watcher {tag} {state}", typeof(TS).Name, tag, update.State);
                    break;
            }
        }

        private async Task OnEvent(WatcherEvent<V1Secret> e)
        {
            var id = KubernetesObjectId.For(e.Item.Metadata());
            var item = e.Item;

            _logger.LogTrace("[{eventType}] {kind} {@id}", e.Type, e.Item.Kind, id);

            if (e.Type != WatchEventType.Added && e.Type != WatchEventType.Modified) return;
            if (!e.Item.Metadata.ReflectionAllowed() || !e.Item.Metadata.FortiReflectionEnabled()) return;
            if (!e.Item.Type.Equals("kubernetes.io/tls", StringComparison.InvariantCultureIgnoreCase)) return;


            var caCrt = Encoding.Default.GetString(item.Data["ca.crt"]);
            var tlsCrt = Encoding.Default.GetString(item.Data["tls.crt"]);
            var tlsKey = Encoding.Default.GetString(item.Data["tls.key"]);
            var tlsCerts = tlsCrt.Split(new[] { "-----END CERTIFICATE-----" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.TrimStart())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => $"{s}-----END CERTIFICATE-----")
                .ToList();


            var hostSecretIds = item.Metadata.FortiReflectionHosts().Select(s => new KubernetesObjectId(s)).ToList();
            var fortiCertName = item.Metadata.FortiCertificate();
            var fortiCertId = !string.IsNullOrWhiteSpace(fortiCertName)
                ? fortiCertName
                : item.Metadata.Name.Substring(0, Math.Min(item.Metadata.Name.Length, 30));

            foreach (var hostSecretId in hostSecretIds)
            {
                _logger.LogDebug(
                    "Reflecting {secretId} to FortiOS device using host secret {hostSecretId}.",
                    id, hostSecretId, hostSecretId);
                string fortiHost;
                string fortiUsername;
                string fortiPassword;
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
                            id, hostSecretId, hostSecretId);
                        continue;
                    }

                    fortiHost = hostSecret.Data.ContainsKey("host")
                        ? Encoding.Default.GetString(hostSecret.Data["host"])
                        : null;
                    fortiUsername = hostSecret.Data.ContainsKey("username")
                        ? Encoding.Default.GetString(hostSecret.Data["username"])
                        : null;
                    fortiPassword = hostSecret.Data.ContainsKey("password")
                        ? Encoding.Default.GetString(hostSecret.Data["password"])
                        : null;
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Cannot reflect {secretId} to {hostSecretId}. Host secret {hostSecretId} not found.",
                        id, hostSecretId, hostSecretId);

                    continue;
                }

                if (string.IsNullOrWhiteSpace(fortiHost) || string.IsNullOrWhiteSpace(fortiUsername) ||
                    string.IsNullOrWhiteSpace(fortiPassword))
                {
                    _logger.LogWarning(
                        "Cannot reflect {secretId} to FortiOS device using host secret {hostSecretId}. " +
                        "Host secret {hostSecretId} must contain 'host', 'username' and 'password' values.",
                        id, hostSecretId, hostSecretId);
                    continue;
                }

                var hostParts = fortiHost.Split(new[] { ":" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (!hostParts.Any() || hostParts.Count > 2)
                {
                    _logger.LogWarning(
                        "Cannot reflect {secretId} to FortiOS device using host secret {hostSecretId}. " +
                        "Host secret {hostSecretId} contains invalid 'host' data. " +
                        "'host' must be in the format 'host:port' where port is optional.",
                        id, hostSecretId, hostSecretId);
                }

                var hostPort = hostParts.Count < 2 ? 22 : int.TryParse(hostParts.Last(), out var port) ? port : 22;

                using var client = new SshClient(hostParts.First(), hostPort, fortiUsername, fortiPassword)
                {
                    ConnectionInfo =
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
                };

                try
                {
                    _logger.LogDebug("Connecting for FortiOS device at {host}", fortiHost);
                    client.Connect();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "Cannot reflect {secretId} to FortiOS device using host secret {hostSecretId} due to exception.",
                        id, hostSecretId);
                    continue;
                }



                _logger.LogDebug("Checking for certificate {certId} on FortiOS device {host}", fortiCertId, fortiHost);
                string checkOutput;
                await using (var shell = client.CreateShellStream(nameof(FortiMirror), 128, 1024, 800, 600, 1024))
                {
                    var lastLine = string.Empty;
                    var outputBuilder = new StringBuilder();
                    shell.WriteLine("config vpn certificate local");
                    shell.WriteLine($"edit {fortiCertId}");
                    shell.WriteLine("show full");
                    shell.WriteLine("end");

                    while (!(lastLine.Contains("#") && lastLine.Contains("end")))
                    {
                        lastLine = shell.ReadLine();
                        outputBuilder.AppendLine(lastLine);
                    }
                    shell.Close();
                    checkOutput = outputBuilder.ToString();

                }

                var localCertificateUpToDate = true;

                if (!string.IsNullOrWhiteSpace(tlsKey))
                {
                    if (checkOutput.Contains("unset private-key"))
                    {
                        localCertificateUpToDate = false;
                    }
                }

                if (!checkOutput.Contains("set certificate", StringComparison.InvariantCultureIgnoreCase))
                {
                    localCertificateUpToDate = false;
                }
                else
                {
                    var remoteCertificate = checkOutput.Substring(checkOutput.IndexOf("set certificate \"",
                        StringComparison.InvariantCultureIgnoreCase));
                    remoteCertificate = remoteCertificate.Replace("set certificate \"", string.Empty,
                        StringComparison.InvariantCultureIgnoreCase);
                    remoteCertificate = remoteCertificate.Substring(0,
                        remoteCertificate.IndexOf("\"", StringComparison.InvariantCultureIgnoreCase));
                    remoteCertificate = remoteCertificate.Replace("\r", string.Empty);

                    if (!tlsCerts.Select(s => s.Replace("\r", string.Empty)).Contains(remoteCertificate))
                    {
                        localCertificateUpToDate = false;
                    }
                }

                var success = true;

                if (!localCertificateUpToDate)
                {
                    var commandBuilder = new StringBuilder();
                    commandBuilder.AppendLine("config vpn certificate local");
                    commandBuilder.AppendLine($"edit {fortiCertId}");
                    commandBuilder.AppendLine($"set private-key \"{tlsKey}\"");
                    commandBuilder.AppendLine($"set certificate \"{tlsCrt}\"");
                    commandBuilder.AppendLine("end");
                    var command = client.RunCommand(commandBuilder.ToString());
                    if (command.ExitStatus != 0 || !string.IsNullOrWhiteSpace(command.Error))
                    {
                        _logger.LogWarning(
                            "Checking for certificate {certId} could not be installed on FortiOS device {host} due to error: {error}",
                            id, fortiHost, command.Error);
                        success = false;
                    }
                }

                if (!localCertificateUpToDate && success)
                {
                    var caCerts = tlsCerts.ToList();
                    if (!string.IsNullOrWhiteSpace(caCrt)) caCerts.Add(caCrt);
                    var caId = 0;
                    for (var i = 0; i < caCerts.Count; i++)
                    {
                        _logger.LogDebug("Installing CA certificate {index} for {certId} on FortiOS device {host}",
                            i + 1, id, fortiHost);

                        var commandBuilder = new StringBuilder();
                        commandBuilder.AppendLine("config vpn certificate ca");
                        commandBuilder.AppendLine($"edit {fortiCertId}_CA{(caId == 0 ? string.Empty : caId.ToString())}");
                        commandBuilder.AppendLine($"set ca \"{tlsCerts[i]}\"");
                        commandBuilder.AppendLine("end");
                        var command = client.RunCommand(commandBuilder.ToString());
                        if (command.ExitStatus == 0 && string.IsNullOrWhiteSpace(command.Error))
                        {
                            caId++;
                            continue;
                        }

                        if (command.Error.Contains("This CA certificate is duplicated."))
                        {
                            _logger.LogWarning("Skipping CA certificate {index} since it is duplicated by another certificate.",
                                i + i);
                        }
                        else if (command.Error.Contains("Input is not a valid CA certificate."))
                        {
                            _logger.LogDebug("Skipping CA certificate {index} since it is not a valid CA certificate.",
                                i + i);
                        }
                        else
                        {
                            _logger.LogWarning("Could not install CA {index} certificate due to error: {response}",
                                i + 1, command.Result);
                            success = false;
                        }
                    }

                }


                if (!success)
                {
                    _logger.LogError("Reflecting {secretId} to FortiOS device using host secret {hostSecretId} completed with errors.",
                        id, hostSecretId);
                }
                else if (!localCertificateUpToDate)
                {
                    _logger.LogInformation("Reflected {secretId} to FortiOS device using host secret {hostSecretId}.",
                        id, hostSecretId);
                }
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
}
