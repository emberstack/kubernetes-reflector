using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core;

public class SecretWatcher : WatcherBackgroundService<V1Secret, V1SecretList>
{
    public SecretWatcher(ILogger<SecretWatcher> logger, IMediator mediator, IKubernetes client) : base(logger, mediator,
        client)
    {
    }


    protected override Task<HttpOperationResponse<V1SecretList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true,
            cancellationToken: cancellationToken);
    }
}