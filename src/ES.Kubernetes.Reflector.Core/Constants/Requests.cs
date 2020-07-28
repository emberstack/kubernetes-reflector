using System;

namespace ES.Kubernetes.Reflector.Core.Constants
{
    public static class Requests
    {
        public static int DefaultTimeout { get; } = (int) TimeSpan.FromHours(1).TotalSeconds;
    }
}