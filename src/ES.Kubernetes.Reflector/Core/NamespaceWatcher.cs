using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core;

public class NamespaceWatcher : WatcherBackgroundService<V1Namespace, V1NamespaceList>
{
    public NamespaceWatcher(ILogger<NamespaceWatcher> logger, IMediator mediator, IKubernetes client) : base(logger,
        mediator, client)
    {
    }


    protected override Task<HttpOperationResponse<V1NamespaceList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.ListNamespaceWithHttpMessagesAsync(watch: true, cancellationToken: cancellationToken);
    }
}