using System;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.CertManager.Constants;
using ES.Kubernetes.Reflector.CertManager.Events;
using ES.Kubernetes.Reflector.CertManager.Resources;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Queuing;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class Monitor : IHostedService, IHealthCheck
    {
        private readonly ManagedWatcher<Certificate> _certificatesWatcher;
        private readonly ManagedWatcher<V1beta1CustomResourceDefinition> _crdWatcher;
        private readonly FeederQueue<WatcherEvent> _eventQueue;
        private readonly ILogger<Monitor> _logger;
        private readonly IMediator _mediator;
        private readonly ManagedWatcher<V1Secret> _secretsWatcher;

        private string _crdVersion;

        public Monitor(ILogger<Monitor> logger,
            ManagedWatcher<V1beta1CustomResourceDefinition> crdWatcher,
            ManagedWatcher<Certificate> certificatesWatcher,
            ManagedWatcher<V1Secret> secretsWatcher,
            IMediator mediator)
        {
            _logger = logger;
            _crdWatcher = crdWatcher;
            _certificatesWatcher = certificatesWatcher;
            _secretsWatcher = secretsWatcher;
            _mediator = mediator;

            _eventQueue = new FeederQueue<WatcherEvent>(OnEvent, OnEventHandlingError);


            _secretsWatcher.OnStateChanged = OnWatcherStateChanged;
            _secretsWatcher.EventHandlerFactory = e =>
                _eventQueue.FeedAsync(new InternalSecretWatcherEvent
                { Item = e.Item, Type = e.Type, CertificateResourceDefinitionVersion = _crdVersion });
            _secretsWatcher.RequestFactory = async c =>
                await c.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true);


            _certificatesWatcher.OnStateChanged = OnWatcherStateChanged;
            _certificatesWatcher.EventHandlerFactory = e =>
                _eventQueue.FeedAsync(new InternalCertificateWatcherEvent
                { Item = e.Item, Type = e.Type, CertificateResourceDefinitionVersion = _crdVersion });


            _crdWatcher.EventHandlerFactory = OnCrdEvent;
            _crdWatcher.RequestFactory = async c =>
                await c.ListCustomResourceDefinitionWithHttpMessagesAsync(watch: true);
            _crdWatcher.OnStateChanged = async (sender, update) =>
            {
                switch (update.State)
                {
                    case ManagedWatcherState.Closed:
                        _logger.LogDebug("{type} watcher {state}", typeof(V1beta1CustomResourceDefinition).Name,
                            update.State);
                        await sender.Start();
                        break;
                    case ManagedWatcherState.Faulted:
                        _logger.LogError(update.Exception, "{type} watcher {state}",
                            typeof(V1beta1CustomResourceDefinition).Name, update.State);
                        break;
                    default:
                        _logger.LogDebug("{type} watcher {state}", typeof(V1beta1CustomResourceDefinition).Name,
                            update.State);
                        break;
                }
            };
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _crdWatcher.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _crdWatcher.Stop();
            await _certificatesWatcher.Stop();
            await _secretsWatcher.Stop();
        }

        private async Task OnWatcherStateChanged<TS>(ManagedWatcher<TS, WatcherEvent<TS>> sender,
            ManagedWatcherStateUpdate update) where TS : class, IKubernetesObject
        {
            switch (update.State)
            {
                case ManagedWatcherState.Closed:
                    _logger.LogDebug("{type} watcher {state}", typeof(TS).Name, update.State);
                    await _secretsWatcher.Stop();
                    await _certificatesWatcher.Stop();

                    await _eventQueue.WaitAndClear();

                    await _secretsWatcher.Start();
                    await _certificatesWatcher.Start();
                    break;
                case ManagedWatcherState.Faulted:
                    _logger.LogError(update.Exception, "{type} watcher {state}", typeof(TS).Name, update.State);
                    break;
                default:
                    _logger.LogDebug("{type} watcher {state}", typeof(TS).Name, update.State);
                    break;
            }
        }

        private async Task OnEvent(WatcherEvent e)
        {
            var id = KubernetesObjectId.For(e.Item.Metadata());
            _logger.LogTrace("[{eventType}] {kind} {@id}", e.Type, e.Item.Kind, id);
            await _mediator.Publish(e);
        }

        private async Task OnEventHandlingError(WatcherEvent e, Exception ex)
        {
            var id = KubernetesObjectId.For(e.Item.Metadata());
            _logger.LogError(ex, "Failed to process {eventType} {kind} {@id} due to exception",
                e.Type, e.Item.Kind, id);
            await _secretsWatcher.Stop();
            await _certificatesWatcher.Stop();
            _eventQueue.Clear();

            _logger.LogTrace("Watchers restarting");
            await _secretsWatcher.Start();
            await _certificatesWatcher.Start();
            _logger.LogTrace("Watchers restarted");
        }


        private async Task OnCrdEvent(WatcherEvent<V1beta1CustomResourceDefinition> request)
        {
            if (request.Type != WatchEventType.Added && request.Type != WatchEventType.Modified) return;
            if (request.Item.Spec?.Names == null) return;
            if (request.Item.Spec.Group != CertManagerConstants.CrdGroup ||
                request.Item.Spec.Names.Kind != CertManagerConstants.CertificateKind) return;
            if (request.Item.Spec.Version == _crdVersion) return;

            _crdVersion = request.Item.Spec.Version;
            _logger.LogInformation("{crdType} {kind} version updated to {crdGroup}/{version}",
                typeof(V1beta1CustomResourceDefinition).Name,
                CertManagerConstants.CertificateKind,
                CertManagerConstants.CrdGroup,
                request.Item.Spec.Version);

            await _certificatesWatcher.Stop();
            await _secretsWatcher.Stop();

            _certificatesWatcher.RequestFactory = async client =>
                await client.ListClusterCustomObjectWithHttpMessagesAsync(request.Item.Spec.Group,
                    request.Item.Spec.Version, request.Item.Spec.Names.Plural, watch: true, timeoutSeconds: (int)TimeSpan.FromHours(1).TotalSeconds);

            await _certificatesWatcher.Start();
            await _secretsWatcher.Start();
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(_crdWatcher.IsFaulted || _secretsWatcher.IsFaulted || _certificatesWatcher.IsFaulted
                ? HealthCheckResult.Unhealthy()
                : HealthCheckResult.Healthy());
        }
    }
}