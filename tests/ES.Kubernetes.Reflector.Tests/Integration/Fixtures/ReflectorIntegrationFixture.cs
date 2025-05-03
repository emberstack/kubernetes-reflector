using ES.Kubernetes.Reflector.Tests.Fixtures;

namespace ES.Kubernetes.Reflector.Tests.Integration.Fixtures;

public class ReflectorIntegrationFixture : IAsyncLifetime
{
    public KubernetesFixture Kubernetes { get; init; } = new();
    public ReflectorFixture Reflector { get; init; } = new();

    public async ValueTask InitializeAsync()
    {
        await Kubernetes.InitializeAsync();
        Reflector.KubernetesClientConfiguration =
            await Kubernetes.GetKubernetesClientConfiguration();
        await Reflector.InitializeAsync();
        Reflector.CreateClient();
    }

    public async ValueTask DisposeAsync() => await Task.CompletedTask;
}