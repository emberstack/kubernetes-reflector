using k8s;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Watchers.Core.Events;

public class WatcherEvent
{
    public WatchEventType EventType { get; set; }
    public IKubernetesObject<V1ObjectMeta>? Item { get; set; }
}