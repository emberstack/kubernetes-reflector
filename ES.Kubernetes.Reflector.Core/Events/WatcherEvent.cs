using k8s;
using MediatR;

namespace ES.Kubernetes.Reflector.Core.Events
{
    public class WatcherEvent<T> : INotification where T : IKubernetesObject
    {
        public T Item { get; set; }
        public WatchEventType Type { get; set; }
    }
}