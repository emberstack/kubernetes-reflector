using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers;

public class NamespaceWatcher(
    ILogger<NamespaceWatcher> logger,
    IMediator mediator,
    IKubernetes kubernetes,
    IOptionsMonitor<ReflectorOptions> options)
    : WatcherBackgroundService<V1Namespace, V1NamespaceList>(logger, mediator, options)
{
    protected override Task<HttpOperationResponse<V1NamespaceList>> OnGetWatcher(CancellationToken cancellationToken) =>
        kubernetes.CoreV1.ListNamespaceWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
}