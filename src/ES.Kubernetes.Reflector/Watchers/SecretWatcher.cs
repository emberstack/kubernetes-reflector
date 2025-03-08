using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers;

public class SecretWatcher(
    ILogger<SecretWatcher> logger,
    IMediator mediator,
    IKubernetes kubernetes,
    IOptionsMonitor<ReflectorOptions> options)
    : WatcherBackgroundService<V1Secret, V1SecretList>(logger, mediator, options)
{
    protected override Task<HttpOperationResponse<V1SecretList>> OnGetWatcher(CancellationToken cancellationToken) =>
        kubernetes.CoreV1.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true,
            timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);

    protected override Task<bool> OnResourceIgnoreCheck(V1Secret item)
    {
        //Skip helm version secrets. This can cause a terrible amount of traffic.
        var ignore = item.Type.StartsWith("helm.sh");
        return Task.FromResult(ignore);
    }
}