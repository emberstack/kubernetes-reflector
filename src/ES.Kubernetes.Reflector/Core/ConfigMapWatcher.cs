using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Watchers;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core;

public class ConfigMapWatcher : WatcherBackgroundService<V1ConfigMap, V1ConfigMapList>
{
    public ConfigMapWatcher(ILogger<ConfigMapWatcher> logger, IMediator mediator, IKubernetes client,
        IOptionsMonitor<ReflectorOptions> options) :
        base(logger, mediator, client, options)
    {
    }


    protected override Task<HttpOperationResponse<V1ConfigMapList>> OnGetWatcher(CancellationToken cancellationToken)
    {
        return Client.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true, timeoutSeconds: WatcherTimeout,
            cancellationToken: cancellationToken);
    }
}