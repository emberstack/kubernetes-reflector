using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Watchers;
using k8s.Autorest;
using k8s.Models;
using k8s;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core;

public class NetworkPolicyWatcher : WatcherBackgroundService<V1NetworkPolicy, V1NetworkPolicyList>
{
    public NetworkPolicyWatcher(ILogger<NetworkPolicyWatcher> logger, IMediator mediator, IKubernetes client,
        IOptionsMonitor<ReflectorOptions> options) :
        base(logger, mediator, client, options)
    {
    }


    protected override Task<HttpOperationResponse<V1NetworkPolicyList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.NetworkingV1.ListNetworkPolicyForAllNamespacesWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}
