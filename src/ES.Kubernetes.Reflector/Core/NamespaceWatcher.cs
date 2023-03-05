using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core;

public class NamespaceWatcher : WatcherBackgroundService<V1Namespace, V1NamespaceList>
{
    public NamespaceWatcher(ILogger<NamespaceWatcher> logger, IMediator mediator, IKubernetes client,
        IOptionsMonitor<ReflectorOptions> options) :
        base(logger, mediator, client, options)
    {
    }


    protected override Task<HttpOperationResponse<V1NamespaceList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.CoreV1.ListNamespaceWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}