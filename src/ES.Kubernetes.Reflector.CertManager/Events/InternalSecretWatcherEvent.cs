using ES.Kubernetes.Reflector.Core.Events;
using k8s.Models;

namespace ES.Kubernetes.Reflector.CertManager.Events
{
    public class InternalSecretWatcherEvent : WatcherEvent<V1Secret>
    {
        public string CertificateResourceDefinitionVersion { get; set; }
    }
}