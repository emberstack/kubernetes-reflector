using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers;

public class NamespaceWatcher(
    ILogger<NamespaceWatcher> logger,
    IKubernetes kubernetes,
    IOptionsMonitor<ReflectorOptions> options,
    IEnumerable<IWatcherEventHandler> watcherEventHandlers,
    IEnumerable<IWatcherClosedHandler> watcherClosedHandlers)
    : WatcherBackgroundService<V1Namespace, V1NamespaceList>(
        logger, options, watcherEventHandlers, watcherClosedHandlers)
{
    protected override IAsyncEnumerable<(WatchEventType, V1Namespace)>
        OnGetWatcher(CancellationToken cancellationToken) =>
        kubernetes.CoreV1.WatchListNamespaceAsync(
            timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
}