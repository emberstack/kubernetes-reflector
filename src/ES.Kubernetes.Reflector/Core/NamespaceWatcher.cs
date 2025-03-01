using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core;

public class NamespaceWatcher(
    ILogger<NamespaceWatcher> logger,
    IMediator mediator,
    IKubernetes kubernetesClient,
    IOptionsMonitor<ReflectorOptions> options)
    : WatcherBackgroundService<V1Namespace, V1NamespaceList>(logger, mediator, options)
{
    protected override Task<HttpOperationResponse<V1NamespaceList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return kubernetesClient.CoreV1.ListNamespaceWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}