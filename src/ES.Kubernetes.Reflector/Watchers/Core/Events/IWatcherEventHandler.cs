namespace ES.Kubernetes.Reflector.Watchers.Core.Events;

public interface IWatcherEventHandler
{
    public Task Handle(WatcherEvent e, CancellationToken cancellationToken);
}