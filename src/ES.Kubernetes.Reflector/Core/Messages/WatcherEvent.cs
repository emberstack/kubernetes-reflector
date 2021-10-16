using k8s;
using k8s.Models;
using MediatR;

namespace ES.Kubernetes.Reflector.Core.Messages;

public class WatcherEvent : INotification
{
    public WatchEventType Type { get; set; }
    public IKubernetesObject<V1ObjectMeta>? Item { get; set; }
}