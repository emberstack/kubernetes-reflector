using System;

namespace ES.Kubernetes.Reflector.Core.Monitoring
{
    public class BroadcastWatcherStateUpdate
    {
        public BroadcastWatcherState State { get; set; }
        public Exception Exception { get; set; }
    }
}