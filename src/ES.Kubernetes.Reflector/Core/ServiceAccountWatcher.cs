using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core;

public class ServiceAccountWatcher : WatcherBackgroundService<V1ServiceAccount, V1ServiceAccountList>
{
    public ServiceAccountWatcher(ILogger<ServiceAccountWatcher> logger, IMediator mediator, IKubernetes client,
        IOptionsMonitor<ReflectorOptions> options) :
        base(logger, mediator, client, options)
    {
    }

    protected override Task<HttpOperationResponse<V1ServiceAccountList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.CoreV1.ListServiceAccountForAllNamespacesWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}