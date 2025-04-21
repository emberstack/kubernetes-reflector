namespace ES.Kubernetes.Reflector.Watchers.Core.Events;

public interface IWatcherClosedHandler
{
    public Task Handle(WatcherClosed e, CancellationToken cancellationToken);
}