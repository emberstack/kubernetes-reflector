using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Metrics;
using ES.Kubernetes.Reflector.Watchers.Core;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers;

public class ConfigMapWatcher(
    ILogger<ConfigMapWatcher> logger,
    IKubernetes kubernetes,
    IOptionsMonitor<ReflectorOptions> options,
    ReflectorMetrics metrics,
    IEnumerable<IWatcherEventHandler> watcherEventHandlers,
    IEnumerable<IWatcherClosedHandler> watcherClosedHandlers)
    : WatcherBackgroundService<V1ConfigMap, V1ConfigMapList>(
        logger, options, metrics, watcherEventHandlers, watcherClosedHandlers)
{
    protected override IAsyncEnumerable<(WatchEventType, V1ConfigMap)> OnGetWatcher(CancellationToken cancellationToken) =>
        kubernetes.CoreV1.WatchListConfigMapForAllNamespacesAsync(
            timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
}