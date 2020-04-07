using System;

namespace ES.Kubernetes.Reflector.Core.Monitoring
{
    public class ManagedWatcherStateUpdate
    {
        public ManagedWatcherState State { get; set; }
        public Exception Exception { get; set; }
    }
}