using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core;

public class RoleWatcher : WatcherBackgroundService<V1Role, V1RoleList>
{
    public RoleWatcher(ILogger<RoleWatcher> logger, IMediator mediator, IKubernetes client,
        IOptionsMonitor<ReflectorOptions> options) :
        base(logger, mediator, client, options)
    {
    }

    protected override Task<HttpOperationResponse<V1RoleList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.RbacAuthorizationV1.ListRoleForAllNamespacesWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}