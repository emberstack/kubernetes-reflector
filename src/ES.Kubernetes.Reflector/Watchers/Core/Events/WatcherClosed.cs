using MediatR;

namespace ES.Kubernetes.Reflector.Watchers.Core.Events;

public class WatcherClosed : INotification
{
    public required Type ResourceType { get; set; }
    public bool Faulted { get; set; }
}