using ES.Kubernetes.Reflector.CertManager.Resources;
using ES.Kubernetes.Reflector.Core.Events;

namespace ES.Kubernetes.Reflector.CertManager.Events
{
    public class InternalCertificateWatcherEvent : WatcherEvent<Certificate>
    {
        public string CertificateResourceDefinitionVersion { get; set; }
    }
}