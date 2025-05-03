using System.Text;
using k8s;
using Testcontainers.K3s;

namespace ES.Kubernetes.Reflector.Tests.Fixtures;

public sealed class KubernetesFixture : IAsyncLifetime
{
    public K3sContainer Container { get; } = new K3sBuilder()
        .WithName($"{nameof(KubernetesFixture)}-{Guid.CreateVersion7()}")
        .Build();

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public async Task<KubernetesClientConfiguration> GetKubernetesClientConfiguration() =>
        await KubernetesClientConfiguration
            .BuildConfigFromConfigFileAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(
                    await Container.GetKubeconfigAsync())));

    public async Task<IKubernetes> GetKubernetesClient() =>
        new k8s.Kubernetes(await GetKubernetesClientConfiguration());
}