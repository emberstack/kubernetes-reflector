namespace ES.Kubernetes.Reflector.Core.Monitoring
{
    public enum ManagedWatcherState
    {
        Stopped,
        Stopping,
        Started,
        Starting,
        Closed,
        Faulted
    }
}