using ES.Kubernetes.Reflector.Tests.Fixtures;

namespace ES.Kubernetes.Reflector.Tests.Integration.Fixtures;

public interface IKubernetesIntegrationFixture
{
    KubernetesFixture Kubernetes { get; }
}
