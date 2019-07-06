using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.CertManager.Constants;
using ES.Kubernetes.Reflector.CertManager.Events;
using ES.Kubernetes.Reflector.CertManager.Resources;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Monitoring;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class CertificatesMonitor : IHostedService,
        INotificationHandler<WatcherEvent<V1beta1CustomResourceDefinition>>,
        IRequestHandler<HealthCheckRequest<CertificatesMonitor>, bool>
    {
        private readonly BroadcastWatcher<Certificate, InternalCertificateWatcherEvent> _certificatesWatcher;
        private readonly IKubernetes _client;
        private readonly ILogger<CertificatesMonitor> _logger;
        private readonly BroadcastWatcher<V1Secret, InternalSecretWatcherEvent> _secretsWatcher;
        private string _certificateResourceDefinitionVersion;

        public CertificatesMonitor(ILogger<CertificatesMonitor> logger,
            BroadcastWatcher<Certificate, InternalCertificateWatcherEvent> certificatesWatcher,
            BroadcastWatcher<V1Secret, InternalSecretWatcherEvent> secretsWatcher,
            IKubernetes client)
        {
            _logger = logger;
            _certificatesWatcher = certificatesWatcher;
            _secretsWatcher = secretsWatcher;
            _client = client;


            _secretsWatcher.OnBeforePublish = e =>
                e.CertificateResourceDefinitionVersion = _certificateResourceDefinitionVersion;
            _secretsWatcher.OnStateChanged = async (sender, update) =>
            {
                switch (update.State)
                {
                    case BroadcastWatcherState.Closed:
                        _logger.LogDebug("Secrets watcher {state}", update.State);
                        await sender.Start();
                        break;
                    case BroadcastWatcherState.Faulted:
                        _logger.LogError(update.Exception, "Secrets watcher {state}", update.State);
                        break;
                    default:
                        _logger.LogDebug("Secrets watcher {state}", update.State);
                        break;
                }
            };

            _certificatesWatcher.OnBeforePublish = e =>
                e.CertificateResourceDefinitionVersion = _certificateResourceDefinitionVersion;
            _certificatesWatcher.OnStateChanged = async (sender, update) =>
            {
                switch (update.State)
                {
                    case BroadcastWatcherState.Closed:
                        _logger.LogDebug("Certificates watcher {state}", update.State);
                        await _secretsWatcher.Stop();
                        await sender.Start();
                        await _secretsWatcher.Start();
                        break;
                    case BroadcastWatcherState.Faulted:
                        _logger.LogError(update.Exception, "Certificates watcher {state}", update.State);
                        break;
                    default:
                        _logger.LogDebug("Certificates watcher {state}", update.State);
                        break;
                }
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting");

            _secretsWatcher.RequestFactory = async client =>
                await client.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true);


            _logger.LogInformation("Started");
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Stopping");
            await _certificatesWatcher.Stop();
            await _secretsWatcher.Stop();
            _logger.LogInformation("Stopped");
        }


        public async Task Handle(WatcherEvent<V1beta1CustomResourceDefinition> request,
            CancellationToken cancellationToken)
        {
            if (request.Type != WatchEventType.Added && request.Type != WatchEventType.Modified) return;
            if (request.Item.Spec?.Names == null) return;
            if (request.Item.Spec.Group != CertManagerConstants.CrdGroup ||
                request.Item.Spec.Names.Kind != CertManagerConstants.CertificateKind) return;

            var resourceDefinition = request.Item;

            _certificateResourceDefinitionVersion = request.Item.Spec.Version;
            _logger.LogInformation("Updating watchers for {kind} version {version}",
                CertManagerConstants.CertificateKind, request.Item.Spec.Version);

            await _certificatesWatcher.Stop();
            await _secretsWatcher.Stop();

            _certificatesWatcher.RequestFactory = async client =>
                await client.ListClusterCustomObjectWithHttpMessagesAsync(request.Item.Spec.Group,
                    request.Item.Spec.Version, request.Item.Spec.Names.Plural, watch: true);

            await _certificatesWatcher.Start();
            await _secretsWatcher.Start();

            _logger.LogInformation("Watchers updated for {kind} version {version}",
                CertManagerConstants.CertificateKind, request.Item.Spec.Version);
        }

        public Task<bool> Handle(HealthCheckRequest<CertificatesMonitor> request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(!_secretsWatcher.IsFaulted && !_certificatesWatcher.IsFaulted);
        }
    }
}