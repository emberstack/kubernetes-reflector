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
    IServiceProvider serviceProvider,
    IOptionsMonitor<ReflectorOptions> options)
    : WatcherBackgroundService<V1Namespace, V1NamespaceList>(logger, mediator, serviceProvider, options)
{
    protected override Task<HttpOperationResponse<V1NamespaceList>> OnGetWatcher(IKubernetes client,
        CancellationToken cancellationToken)
    {
        return client.CoreV1.ListNamespaceWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}