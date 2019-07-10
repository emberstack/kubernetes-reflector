using k8s;
using MediatR;

namespace ES.Kubernetes.Reflector.Core.Events
{
    public class WatcherEvent
    {
        public WatchEventType Type { get; set; }
        public IKubernetesObject Item { get; set; }
    }

    public class WatcherEvent<T> : WatcherEvent, INotification where T : class, IKubernetesObject
    {
        public new T Item
        {
            get => base.Item as T;
            set => base.Item = value;
        }
    }
}