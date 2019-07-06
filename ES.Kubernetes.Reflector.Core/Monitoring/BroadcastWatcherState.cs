namespace ES.Kubernetes.Reflector.Core.Monitoring
{
    public enum BroadcastWatcherState
    {
        Stopped,
        Stopping,
        Started,
        Starting,
        Closed,
        Faulted
    }
}