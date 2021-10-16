using MediatR;

namespace ES.Kubernetes.Reflector.Core.Messages;

public class WatcherClosed : INotification
{
    public Type ResourceType { get; set; } = default!;
    public bool Faulted { get; set; }
}