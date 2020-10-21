using System;

namespace ES.Kubernetes.Reflector.Core.Constants
{
    public static class Requests
    {
        public static int WatcherTimeout { get; } = (int) TimeSpan.FromMinutes(2).TotalSeconds;
    }
}