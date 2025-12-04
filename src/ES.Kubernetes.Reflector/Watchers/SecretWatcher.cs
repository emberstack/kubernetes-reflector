using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers;

public class SecretWatcher(
    ILogger<SecretWatcher> logger,
    IKubernetes kubernetes,
    IOptionsMonitor<ReflectorOptions> options,
    IEnumerable<IWatcherEventHandler> watcherEventHandlers,
    IEnumerable<IWatcherClosedHandler> watcherClosedHandlers)
    : WatcherBackgroundService<V1Secret, V1SecretList>(
        logger, options, watcherEventHandlers, watcherClosedHandlers)
{
    protected override IAsyncEnumerable<(WatchEventType, V1Secret)> OnGetWatcher(CancellationToken cancellationToken) =>
        kubernetes.CoreV1.WatchListSecretForAllNamespacesAsync(
            timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);

    protected override Task<bool> OnResourceIgnoreCheck(V1Secret item)
    {
        //Skip helm version secrets. This can cause a terrible amount of traffic.
        var ignore = item.Type.StartsWith("helm.sh");
        return Task.FromResult(ignore);
    }
}