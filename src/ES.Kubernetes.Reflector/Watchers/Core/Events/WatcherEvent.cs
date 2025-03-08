using k8s;
using k8s.Models;
using MediatR;

namespace ES.Kubernetes.Reflector.Watchers.Core.Events;

public class WatcherEvent : INotification
{
    public WatchEventType EventType { get; set; }
    public IKubernetesObject<V1ObjectMeta>? Item { get; set; }
}