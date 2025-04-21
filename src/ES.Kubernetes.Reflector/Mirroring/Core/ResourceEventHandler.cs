
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Models;
using MediatR;

namespace ES.Kubernetes.Reflector.Mirroring.Core;
/// <summary>
/// This is a transient wrapper around the underlying ResourceMirror instance.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="mirror"></param>
public abstract class ResourceEventHandler<T>(ResourceMirror<T> mirror) : INotificationHandler<WatcherEvent>, INotificationHandler<WatcherClosed>
    where T : class, IKubernetesObject<V1ObjectMeta>
{
    protected readonly ResourceMirror<T> Mirror = mirror;

    public Task Handle(WatcherEvent notification, CancellationToken cancellationToken)
    {
        return Mirror.Handle(notification, cancellationToken);
    }

    public Task Handle(WatcherClosed notification, CancellationToken cancellationToken)
    {
        return Mirror.Handle(notification, cancellationToken);
    }
}
