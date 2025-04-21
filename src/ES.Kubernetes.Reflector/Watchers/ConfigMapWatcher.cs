using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers;

public class ConfigMapWatcher(
    ILogger<ConfigMapWatcher> logger,
    IKubernetes kubernetes,
    IOptionsMonitor<ReflectorOptions> options,
    IEnumerable<IWatcherEventHandler> watcherEventHandlers,
    IEnumerable<IWatcherClosedHandler> watcherClosedHandlers)
    : WatcherBackgroundService<V1ConfigMap, V1ConfigMapList>(
        logger, options, watcherEventHandlers, watcherClosedHandlers)
{
    protected override Task<HttpOperationResponse<V1ConfigMapList>> OnGetWatcher(CancellationToken cancellationToken) =>
        kubernetes.CoreV1.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true,
            timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
}