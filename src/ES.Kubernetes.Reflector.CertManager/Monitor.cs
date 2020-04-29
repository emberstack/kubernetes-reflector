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
using Timer = System.Timers.Timer;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class Monitor : IHostedService, IHealthCheck
    {
        private readonly Func<ManagedWatcher<Certificate, object>> _certificatesWatcherFactory;

        private readonly Dictionary<string, ManagedWatcher<Certificate, object>> _certificatesWatchers =
            new Dictionary<string, ManagedWatcher<Certificate, object>>();

        private readonly ManagedWatcher<V1CustomResourceDefinition, V1CustomResourceDefinitionList> _crdV1Watcher;

        private readonly FeederQueue<WatcherEvent> _eventQueue;
        private readonly ILogger<Monitor> _logger;
        private readonly IMediator _mediator;
        private readonly ManagedWatcher<V1Secret, V1SecretList> _secretsWatcher;
        private readonly IKubernetes _apiClient;

        private readonly Timer _v1Beta1CrdMonitorTimer = new Timer();
        private bool _v1Beta1CrdMonitorTimerFaulted;

        public Monitor(ILogger<Monitor> logger,
            ManagedWatcher<V1CustomResourceDefinition, V1CustomResourceDefinitionList> crdV1Watcher,
            Func<ManagedWatcher<Certificate, object>> certificatesWatcherFactory,
            ManagedWatcher<V1Secret, V1SecretList> secretsWatcher,
            IKubernetes apiClient,
            IMediator mediator)
        {
            _logger = logger;
            _crdV1Watcher = crdV1Watcher;
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


            _crdV1Watcher.EventHandlerFactory = OnCrdEventV1;
            _crdV1Watcher.RequestFactory = async c =>
                await c.ListCustomResourceDefinitionWithHttpMessagesAsync(watch: true);
            _crdV1Watcher.OnStateChanged = OnCrdWatcherStateChanged;

            _v1Beta1CrdMonitorTimer.Elapsed += (_,__)=> Onv1Beta1CrdRefresh();
            _v1Beta1CrdMonitorTimer.Interval = 30_000;
        }


        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(_crdV1Watcher.IsFaulted ||
                                   _v1Beta1CrdMonitorTimerFaulted ||
                                   _secretsWatcher.IsFaulted ||
                                   _certificatesWatchers.Values.Any(s => s.IsFaulted)
                ? HealthCheckResult.Unhealthy()
                : HealthCheckResult.Healthy());
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _apiClient.ListCustomResourceDefinitionAsync(cancellationToken: cancellationToken);
                await _crdV1Watcher.Start();
            }
            catch (HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "Current kubernetes version does not support {type} apiVersion {version}.",
                    V1CustomResourceDefinition.KubeKind, V1CustomResourceDefinition.KubeApiVersion);
                Onv1Beta1CrdRefresh();
                _v1Beta1CrdMonitorTimer.Start();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _crdV1Watcher.Stop();
            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Stop();
            await _secretsWatcher.Stop();
        }

        private void Onv1Beta1CrdRefresh()
        {
            try
            {
                _logger.LogDebug(
                    "Updating {type} {kind} in group {group}",
                    typeof(V1beta1CustomResourceDefinition).Name,
                    CertManagerConstants.CertificateKind, CertManagerConstants.CrdGroup);

                _v1Beta1CrdMonitorTimerFaulted = false;
                var crd = _apiClient.ListCustomResourceDefinition1().Items
                    .FirstOrDefault(s =>
                        s.Spec?.Names != null && s.Spec.Group == CertManagerConstants.CrdGroup &&
                        s.Spec.Names.Kind == CertManagerConstants.CertificateKind);

                if (crd == null) return;
                OnCrdVersionUpdate(crd.GetType().Name, crd.Spec.Names.Kind,
                    crd.Spec.Group, crd.Spec.Names.Plural, crd.Spec.Versions.Select(s => s.Name).ToList()).Wait();

                
            }
            catch (HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "Current kubernetes version does not support {type} apiVersion {version}.",
                    V1beta1CustomResourceDefinition.KubeKind, V1beta1CustomResourceDefinition.KubeApiVersion);

                _v1Beta1CrdMonitorTimer.Stop();
            }
            catch (Exception exception)
            {
                _v1Beta1CrdMonitorTimerFaulted = true;
                _v1Beta1CrdMonitorTimer.Stop();
                _logger.LogError(exception,
                    "Error occured while getting {kind} version {version}",
                    V1beta1CustomResourceDefinition.KubeKind, V1beta1CustomResourceDefinition.KubeApiVersion);
            }


        }

        private async Task OnCrdWatcherStateChanged<TResource, TResourceList>(
            ManagedWatcher<TResource, TResourceList, WatcherEvent<TResource>> sender, ManagedWatcherStateUpdate update)
            where TResource : class, IKubernetesObject
        {
            switch (update.State)
            {
                case ManagedWatcherState.Closed:
                    _logger.LogDebug("{type} watcher {state}", typeof(TResource).Name, update.State);
                    await sender.Start();
                    break;
                case ManagedWatcherState.Faulted:
                    _logger.LogError(update.Exception, "{type} watcher {state}",
                        typeof(TResource).Name, update.State);
                    break;
                default:
                    _logger.LogDebug("{type} watcher {state}", typeof(TResource).Name, update.State);
                    break;
            }
        }

        private async Task OnWatcherStateChanged<TResource, TResourceList>(ManagedWatcher<TResource, TResourceList, WatcherEvent<TResource>> sender,
            ManagedWatcherStateUpdate update) where TResource : class, IKubernetesObject
        {
            var tag = sender.Tag ?? string.Empty;
            switch (update.State)
            {
                case ManagedWatcherState.Closed:
                    _logger.LogDebug("{type} watcher {tag} {state}", typeof(TResource).Name, tag, update.State);
                    await _secretsWatcher.Stop();
                    foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Stop();

                    await _eventQueue.WaitAndClear();

                    await _secretsWatcher.Start();
                    foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Start();
                    break;
                case ManagedWatcherState.Faulted:
                    _logger.LogError(update.Exception, "{type} watcher {tag} {state}", typeof(TResource).Name, tag,
                        update.State);
                    break;
                default:
                    _logger.LogDebug("{type} watcher {tag} {state}", typeof(TResource).Name, tag, update.State);
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

            if(ex is Microsoft.Rest.HttpOperationException httpEx)
            {
                _logger.LogError($"Microsoft.Rest.HttpOperationException response: {httpEx.Response.Content}");
            }

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


        private async Task OnCrdEventV1(WatcherEvent<V1CustomResourceDefinition> request)
        {
            if (request.Type != WatchEventType.Added && request.Type != WatchEventType.Modified) return;
            if (request.Item.Spec?.Names == null) return;

            if (request.Item.Spec.Group != CertManagerConstants.CrdGroup ||
                request.Item.Spec.Names.Kind != CertManagerConstants.CertificateKind) return;
            var versions = request.Item.Spec.Versions.Select(s => s.Name).ToList();
            if (versions.TrueForAll(s => _certificatesWatchers.ContainsKey(s))) return;

            await OnCrdVersionUpdate(request.Item.GetType().Name, request.Item.Spec.Names.Kind, request.Item.Spec.Group,
                request.Item.Spec.Names.Plural, versions);
        }

        private async Task OnCrdVersionUpdate(string crdType, string crdKind, string crdGroup, string crdPlural, List<string> versions)
        {
            if (versions.TrueForAll(s => _certificatesWatchers.ContainsKey(s))) return;

            _logger.LogInformation("{crdType} {kind} in group {group} versions updated to {versions}",
                crdType, crdKind, crdGroup, versions);

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
                    crdGroup, version, crdPlural, watch: true,
                    timeoutSeconds: (int)TimeSpan.FromHours(1).TotalSeconds);
                _certificatesWatchers.Add(version, watcher);
            }


            foreach (var certificatesWatcher in _certificatesWatchers.Values) await certificatesWatcher.Start();
            await _secretsWatcher.Start();
        }
    }
}