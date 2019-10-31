using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class Monitor : IHostedService, IHealthCheck
    {
        private readonly Func<ManagedWatcher<Certificate, object>> _certificatesWatcherFactory;

        private readonly Dictionary<string, ManagedWatcher<Certificate, object>> _certificatesWatchers =
            new Dictionary<string, ManagedWatcher<Certificate, object>>();

        private readonly ManagedWatcher<V1CustomResourceDefinition, V1CustomResourceDefinitionList> _crdWatcher;
        private readonly FeederQueue<WatcherEvent> _eventQueue;
        private readonly ILogger<Monitor> _logger;
        private readonly IMediator _mediator;
        private readonly ManagedWatcher<V1Secret, V1SecretList> _secretsWatcher;
        private readonly IKubernetes _apiClient;

        public Monitor(ILogger<Monitor> logger,
            ManagedWatcher<V1CustomResourceDefinition, V1CustomResourceDefinitionList> crdWatcher,
            Func<ManagedWatcher<Certificate, object>> certificatesWatcherFactory,
            ManagedWatcher<V1Secret, V1SecretList> secretsWatcher,
            IKubernetes apiClient,
            IMediator mediator)
        {
            _logger = logger;
            _crdWatcher = crdWatcher;
            _certificatesWatcherFactory = certificatesWatcherFactory;
            _secretsWatcher = secretsWatcher;
            _apiClient = apiClient;
            _mediator = mediator;

            _eventQueue = new FeederQueue<WatcherEvent>(OnEvent, OnEventHandlingError);


            _secretsWatcher.OnStateChanged = OnWatcherStateChanged;
            _secretsWatcher.EventHandlerFactory = e =>
                _eventQueue.FeedAsync(new InternalSecretWatcherEvent
                {
                    Item = e.Item,
                    Type = e.Type,
                    CertificateResourceDefinitionVersions = _certificatesWatchers.Keys.ToList()
                });
            _secretsWatcher.RequestFactory = async c =>
                await c.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true);


            _crdWatcher.EventHandlerFactory = OnCrdEvent;
            _crdWatcher.RequestFactory = async c =>
                await c.ListCustomResourceDefinitionWithHttpMessagesAsync(watch: true);
            _crdWatcher.OnStateChanged = async (sender, update) =>
            {
                switch (update.State)
                {
                    case ManagedWatcherState.Closed:
                        _logger.LogDebug("{type} watcher {state}", typeof(V1CustomResourceDefinition).Name,
                            update.State);
                        await sender.Start();
                        break;
                    case ManagedWatcherState.Faulted:
                        _logger.LogError(update.Exception, "{type} watcher {state}",
                            typeof(V1CustomResourceDefinition).Name, update.State);
                        break;
                    default:
                        _logger.LogDebug("{type} watcher {state}", typeof(V1CustomResourceDefinition).Name,
                            update.State);
                        break;
                }
            };
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(_crdWatcher.IsFaulted || _secretsWatcher.IsFaulted ||
                                   _certificatesWatchers.Values.Any(s => s.IsFaulted)
                ? HealthCheckResult.Unhealthy()
                : HealthCheckResult.Healthy());
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _apiClient.ListCustomResourceDefinitionAsync(cancellationToken: cancellationToken);
                await _crdWatcher.Start();
            }
            catch (HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError(
                    "Current kubernetes version does not support {type} apiVersion {version}.",
                    V1CustomResourceDefinition.KubeKind, V1CustomResourceDefinition.KubeApiVersion);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _crdWatcher.Stop();
            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Stop();
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
                    foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Stop();

                    await _eventQueue.WaitAndClear();

                    await _secretsWatcher.Start();
                    foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Start();
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
            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Stop();
            _eventQueue.Clear();

            _logger.LogTrace("Watchers restarting");
            await _secretsWatcher.Start();
            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Start();
            _logger.LogTrace("Watchers restarted");
        }


        private async Task OnCrdEvent(WatcherEvent<V1CustomResourceDefinition> request)
        {
            if (request.Type != WatchEventType.Added && request.Type != WatchEventType.Modified) return;
            if (request.Item.Spec?.Names == null) return;

            if (request.Item.Spec.Group != CertManagerConstants.CrdGroup ||
                request.Item.Spec.Names.Kind != CertManagerConstants.CertificateKind) return;
            var versions = request.Item.Spec.Versions.Select(s => s.Name).ToList();
            if (versions.TrueForAll(s => _certificatesWatchers.ContainsKey(s))) return;

            _logger.LogInformation("{crdType} {kind} in group {group} versions updated to {versions}",
                typeof(V1CustomResourceDefinition).Name,
                request.Item.Spec.Names.Kind,
                request.Item.Spec.Group,
                versions);

            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Stop();
            await _secretsWatcher.Stop();

            _certificatesWatchers.Clear();

            foreach (var version in versions)
            {
                var watcher = _certificatesWatcherFactory();
                watcher.Tag = version;
                watcher.OnStateChanged = OnWatcherStateChanged;
                watcher.EventHandlerFactory = e =>
                    _eventQueue.FeedAsync(new InternalCertificateWatcherEvent { Item = e.Item, Type = e.Type });
                watcher.RequestFactory = async client => await client.ListClusterCustomObjectWithHttpMessagesAsync(
                    request.Item.Spec.Group,
                    version, request.Item.Spec.Names.Plural, watch: true,
                    timeoutSeconds: (int)TimeSpan.FromHours(1).TotalSeconds);
                _certificatesWatchers.Add(version, watcher);
            }


            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Start();
            await _secretsWatcher.Start();
        }
    }
}