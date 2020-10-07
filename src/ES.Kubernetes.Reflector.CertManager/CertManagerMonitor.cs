using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.CertManager.Constants;
using ES.Kubernetes.Reflector.CertManager.Events;
using ES.Kubernetes.Reflector.CertManager.Resources;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Queuing;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using k8s.Versioning;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json.Linq;
using Timer = System.Timers.Timer;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class CertManagerMonitor : IHostedService, IHealthCheck
    {
        private readonly IKubernetes _apiClient;
        private readonly ManagedWatcher<Certificate, object> _certificateWatcher;

        private readonly Timer _certManagerTimer = new Timer();


        private readonly FeederQueue<WatcherEvent> _eventQueue;
        private readonly ILogger<CertManagerMonitor> _logger;
        private readonly IMediator _mediator;
        private readonly ManagedWatcher<V1Secret, V1SecretList> _secretsWatcher;

        private Channel<object> _monitorTriggerChannel;

        private string _version;

        public CertManagerMonitor(ILogger<CertManagerMonitor> logger,
            ManagedWatcher<Certificate, object> certificateWatcher,
            ManagedWatcher<V1Secret, V1SecretList> secretsWatcher,
            IKubernetes apiClient,
            IMediator mediator)
        {
            _logger = logger;
            _certificateWatcher = certificateWatcher;
            _secretsWatcher = secretsWatcher;
            _apiClient = apiClient;
            _mediator = mediator;

            _eventQueue = new FeederQueue<WatcherEvent>(OnEvent, OnEventHandlingError);


            _secretsWatcher.OnStateChanged = OnWatcherStateChanged;
            _secretsWatcher.EventHandlerFactory = e =>
            {
                if (e.Item.Type.StartsWith("helm.sh")) return Task.CompletedTask;

                return _eventQueue.FeedAsync(new InternalSecretWatcherEvent
                {
                    Item = e.Item,
                    Type = e.Type,
                    CertificateResourceDefinitionVersion = _version
                });
            };
            _secretsWatcher.RequestFactory = async c =>
                await c.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true,
                    timeoutSeconds: Requests.WatcherTimeout);

            _certManagerTimer.Elapsed += (_, __) => _monitorTriggerChannel.Writer.TryWrite(new object());
            _certManagerTimer.Interval = 5_000;


            _certificateWatcher.OnStateChanged = OnWatcherStateChanged;
            _certificateWatcher.EventHandlerFactory = e =>
                _eventQueue.FeedAsync(new InternalCertificateWatcherEvent {Item = e.Item, Type = e.Type});
            _certificateWatcher.RequestFactory = async client =>
                await client.ListClusterCustomObjectWithHttpMessagesAsync(
                    CertManagerConstants.CrdGroup, _version, CertManagerConstants.CertificatePlural, watch: true,
                    timeoutSeconds: Requests.WatcherTimeout);
        }


        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(_secretsWatcher.IsFaulted || _certificateWatcher.IsFaulted
                ? HealthCheckResult.Unhealthy()
                : HealthCheckResult.Healthy());
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting");
            try
            {
                await _apiClient.ListCustomResourceDefinitionAsync(cancellationToken: cancellationToken);
            }
            catch (HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "Current kubernetes version does not support {type} apiVersion {version}. `cert-manager` extension disabled.",
                    V1CustomResourceDefinition.KubeKind, V1CustomResourceDefinition.KubeApiVersion);
                return;
            }

            var channel = Channel.CreateBounded<object>(new BoundedChannelOptions(1)
                {FullMode = BoundedChannelFullMode.DropNewest});

            async Task ReadChannel()
            {
                while (await channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    await channel.Reader.ReadAsync(cancellationToken);
                    try
                    {
                        await OnCertManagerDiscovery();
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Exception occurred while processing `cert-manager` discovery");
                    }
                }
            }

            var _ = ReadChannel();
            _monitorTriggerChannel = channel;

            await _monitorTriggerChannel.Writer.WriteAsync(new object(), cancellationToken);
            _certManagerTimer.Enabled = true;

            _logger.LogDebug("Started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Stopping");

            _certManagerTimer.Enabled = false;
            await _certificateWatcher.Stop();
            await _secretsWatcher.Stop();
            _monitorTriggerChannel?.Writer.Complete();

            _logger.LogDebug("Stopped");
        }

        private async Task OnCertManagerDiscovery()
        {
            _logger.LogTrace("Searching for cert-manager CRDs");
            //Get all CRDs in order to check for cert-manager specific CRDs
            var customResourceDefinitions = await _apiClient.ListCustomResourceDefinitionAsync();

            //Find the `Certificate` CRD (if installed)
            var certCrd = customResourceDefinitions.Items.FirstOrDefault(s =>
                s.Spec?.Names != null && s.Spec.Group == CertManagerConstants.CrdGroup &&
                s.Spec.Names.Kind == CertManagerConstants.CertificateKind);
            var versions = new List<string>();

            //Get Certificate CRD versions
            if (!(certCrd is null)) versions = certCrd.Spec.Versions.Select(s => s.Name).ToList();

            _logger.LogTrace("Cert-manager CRDs versions found: `{versions}`", versions);

            //Check if at least one Certificate exists (regardless of version) so we can start monitoring
            var version = versions.OrderBy(s => s, KubernetesVersionComparer.Instance).LastOrDefault();
            var certificatesExit = false;
            if (version != null)
            {
                var certList = ((JObject) await _apiClient.ListClusterCustomObjectAsync(CertManagerConstants.CrdGroup,
                    version,
                    CertManagerConstants.CertificatePlural)).ToObject<KubernetesList<Certificate>>();
                certificatesExit = certList.Items.Any();
            }


            //If no certificate exists, stop everything. Timer will periodically check again
            if (!certificatesExit)
            {
                await _certificateWatcher.Stop();
                await _secretsWatcher.Stop();
                _version = null;
                _logger.LogTrace("No {kind} found.", CertManagerConstants.CertificateKind);
                return;
            }

            if (_version != version)
            {
                _logger.LogDebug("{kind} version is {version}", CertManagerConstants.CertificateKind, version);

                await _certificateWatcher.Stop();
                await _secretsWatcher.Stop();
                _version = version;
                if (_version != null)
                {
                    await _certificateWatcher.Start();
                    await _secretsWatcher.Start();
                }
            }
        }

        private async Task OnWatcherStateChanged<TResource, TResourceList>(
            ManagedWatcher<TResource, TResourceList, WatcherEvent<TResource>> sender,
            ManagedWatcherStateUpdate update) where TResource : class, IKubernetesObject
        {
            switch (update.State)
            {
                case ManagedWatcherState.Closed:
                    _logger.LogDebug("{type} watcher {state}", typeof(TResource).Name, update.State);
                    await _secretsWatcher.Stop();
                    await _certificateWatcher.Stop();

                    await _eventQueue.WaitAndClear();

                    await _secretsWatcher.Start();
                    await _certificateWatcher.Start();
                    break;
                case ManagedWatcherState.Faulted:
                    _logger.LogError(update.Exception, "{type} watcher {state}", typeof(TResource).Name,
                        update.State);
                    break;
                default:
                    _logger.LogDebug("{type} watcher {state}", typeof(TResource).Name, update.State);
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
            await _certificateWatcher.Stop();
            _eventQueue.Clear();

            _logger.LogTrace("Watchers restarting");
            await _secretsWatcher.Start();
            await _certificateWatcher.Start();
            _logger.LogTrace("Watchers restarted");
        }
    }
}