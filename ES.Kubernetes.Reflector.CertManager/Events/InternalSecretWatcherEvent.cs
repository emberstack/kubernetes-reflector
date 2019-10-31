using System.Collections.Generic;
using ES.Kubernetes.Reflector.Core.Events;
using k8s.Models;

namespace ES.Kubernetes.Reflector.CertManager.Events
{
    public class InternalSecretWatcherEvent : WatcherEvent<V1Secret>
    {
        public List<string> CertificateResourceDefinitionVersions { get; set; }
    }
}