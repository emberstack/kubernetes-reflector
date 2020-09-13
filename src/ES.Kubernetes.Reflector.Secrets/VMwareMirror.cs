using System;
using System.Linq;
using System.Net;
using System.Text;
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
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ES.Kubernetes.Reflector.Secrets
{
    public class VMwareMirror : IHostedService, IHealthCheck
    {
        private readonly IKubernetes _apiClient;

        private readonly FeederQueue<WatcherEvent<V1Secret>> _eventQueue;
        private readonly ILogger<VMwareMirror> _logger;
        private readonly ManagedWatcher<V1Secret, V1SecretList> _secretsWatcher;

        public VMwareMirror(ILogger<VMwareMirror> logger,
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
            var secretId = KubernetesObjectId.For(e.Item.Metadata());
            var item = e.Item;

            _logger.LogTrace("[{eventType}] {kind} {@id}", e.Type, e.Item.Kind, secretId);

            if (e.Type != WatchEventType.Added && e.Type != WatchEventType.Modified) return;
            if (!e.Item.Metadata.ReflectionAllowed() || !e.Item.Metadata.VMwareReflectionEnabled()) return;
            if (!e.Item.Type.Equals("kubernetes.io/tls", StringComparison.InvariantCultureIgnoreCase)) return;

            _logger.LogDebug("VMware enabled using host secret {secretId}.", secretId);
            
            var tlsCrt = Encoding.Default.GetString(item.Data["tls.crt"]);
            var tlsKey = Encoding.Default.GetString(item.Data["tls.key"]);

            var hostSecretIds =
                item.Metadata.VMwareReflectionHosts()
                    .Select(s => new KubernetesObjectId(s))
                    .ToList();

            foreach (var hostSecretId in hostSecretIds)
            {
                _logger.LogDebug(
                    "Reflecting {secretId} to VMware ESXi device using host secret {hostSecretId}.",
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
                        "Cannot reflect {secretId} to VMware ESXi device using host secret {hostSecretId}. " +
                        "Host secret {hostSecretId} must contain 'host', 'username' and 'password' values.",
                        secretId, hostSecretId, hostSecretId);
                    continue;
                }

                var hostParts = hostAddress.Split(new[] {":"}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (!hostParts.Any() || hostParts.Count > 2)
                    _logger.LogWarning(
                        "Cannot reflect {secretId} to VMware ESXi device using host secret {hostSecretId}. " +
                        "Host secret {hostSecretId} contains invalid 'host' data. " +
                        "'host' must be in the format 'host:port' where port is optional.",
                        secretId, hostSecretId, hostSecretId);

                var host = hostParts.First();
                var port =
                    hostParts.Count < 2
                        ? 22
                        : int.TryParse(hostParts.Last(), out var p)
                            ? p
                            : 22;

                // SSH
                void HandleKeyEvent(object sender, AuthenticationPromptEventArgs eventArgs)
                { 
                    foreach (var prompt in eventArgs.Prompts)
                    {
                        if (prompt.Request.IndexOf("Password:", StringComparison.InvariantCultureIgnoreCase) != -1)
                        {
                            prompt.Response = password;
                        }
                    }
                }
                
                var keyboardAuth = new KeyboardInteractiveAuthenticationMethod(username);
                keyboardAuth.AuthenticationPrompt += HandleKeyEvent;
                var connectionInfo = new ConnectionInfo(host, port, username, keyboardAuth);
                
                using var client = new SshClient(connectionInfo);
                client.ErrorOccurred += delegate(object sender, ExceptionEventArgs exceptionEventArgs)
                {
                    _logger.LogError(exceptionEventArgs.Exception,
                        "Cannot reflect {secretId} to VMware ESXi device using host secret {hostSecretId} due to exception.",
                        secretId, hostSecretId);
                };

                _logger.LogDebug("Connecting to VMware ESXi device at {host}", hostAddress);
                client.Connect();

                _logger.LogDebug("Check certificate on VMware ESXi device at {host}", hostAddress);
                var catCommand = client.RunCommand("cat /etc/vmware/ssl/rui.crt");
                if (catCommand.Result.Contains(tlsCrt))
                {
                    _logger.LogDebug(
                        "Skip reflecting {secretId} to VMware ESXi device using host secret {hostSecretId}. Already exists.",
                        secretId, hostSecretId);
                    return;
                }

                _logger.LogDebug("Configuring new Let's Encrypt certs on VMware ESXi device at {host}", hostAddress);
                client.RunCommand($"echo \"{tlsCrt}\" > /etc/vmware/ssl/rui.crt");
                client.RunCommand($"echo \"{tlsKey}\" > /etc/vmware/ssl/rui.key");

                _logger.LogDebug("Restarting on VMware ESXi device at {host}", hostAddress);
                client.RunCommand("services.sh restart");

                client.Disconnect();

                _logger.LogInformation("Reflected {secretId} to VMware ESXi device using host secret {hostSecretId}.",
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
}