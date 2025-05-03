using System.Net;
using ES.Kubernetes.Reflector.Tests.Additions;
using ES.Kubernetes.Reflector.Tests.Integration.Base;
using ES.Kubernetes.Reflector.Tests.Integration.Fixtures;
using JetBrains.Annotations;
using k8s;
using k8s.Autorest;
using k8s.Models;

[assembly: AssemblyFixture(typeof(ReflectorIntegrationFixture))]

namespace ES.Kubernetes.Reflector.Tests.Integration;

[PublicAPI]
public class MirroringIntegrationTests(
    ReflectorIntegrationFixture integrationFixture)
    : BaseIntegrationTest(integrationFixture)
{
    [Fact]
    public async Task AutoReflect_To_AllowedNamespaces()
    {
        var client = await GetKubernetesClient();

        var allowedNamespaces = new[]
        {
            $"allowed-{Guid.CreateVersion7()}",
            $"allowed-{Guid.CreateVersion7()}"
        };
        var notAllowedNamespaces = new[]
        {
            $"not-allowed-{Guid.CreateVersion7()}",
            $"not-allowed-{Guid.CreateVersion7()}"
        };

        foreach (var ns in allowedNamespaces.Concat(notAllowedNamespaces)) await CreateNamespaceAsync(ns);

        var sourceResource = await CreateResource(client,
            annotations: new ReflectorAnnotationsBuilder()
                .WithReflectionAllowed(true)
                .WithAllowedNamespaces("^allowed-.*")
                .WithAutoEnabled(true).Build());

        await DelayForReflection();


        foreach (var ns in allowedNamespaces)
            Assert.True(await WaitForResource(client, sourceResource.Name(), ns,
                TestContext.Current.CancellationToken));

        foreach (var ns in notAllowedNamespaces)
            Assert.False(await ResourceExists(client,
                sourceResource.Name(), ns,
                TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task AutoReflect_To_NewNamespaces()
    {
        var client = await GetKubernetesClient();

        var allowedNamespaces = new[]
        {
            $"allowed-{Guid.CreateVersion7()}",
            $"allowed-{Guid.CreateVersion7()}"
        };

        var sourceResource = await CreateResource(client,
            annotations: new ReflectorAnnotationsBuilder()
                .WithReflectionAllowed(true)
                .WithAllowedNamespaces("^allowed-.*")
                .WithAutoEnabled(true).Build());

        foreach (var ns in allowedNamespaces) await CreateNamespaceAsync(ns);

        await DelayForReflection();

        foreach (var ns in allowedNamespaces)
            Assert.True(await WaitForResource(client, sourceResource.Name(), ns,
                TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task AutoReflect_Remove_ReflectionsWhenResourceDeleted()
    {
        var client = await GetKubernetesClient();

        var allowedNamespaces = new[]
        {
            $"allowed-{Guid.CreateVersion7()}",
            $"allowed-{Guid.CreateVersion7()}"
        };

        var sourceResource = await CreateResource(client,
            annotations: new ReflectorAnnotationsBuilder()
                .WithReflectionAllowed(true)
                .WithAllowedNamespaces("^allowed-.*")
                .WithAutoEnabled(true).Build());


        foreach (var ns in allowedNamespaces) await CreateNamespaceAsync(ns);

        foreach (var ns in allowedNamespaces)
            Assert.True(await WaitForResource(client, sourceResource.Name(),
                ns, TestContext.Current.CancellationToken));

        await DeleteResource(client, sourceResource.Name(), sourceResource.Namespace(),
            TestContext.Current.CancellationToken);


        await DelayForReflection();

        foreach (var ns in allowedNamespaces)
            Assert.False(await ResourceExists(client,
                sourceResource.Name(), ns,
                TestContext.Current.CancellationToken));
    }


    private async Task<V1Secret> CreateResource(IKubernetes client,
        string? name = null, string? namespaceName = null,
        Dictionary<string, string>? annotations = null,
        Dictionary<string, string>? data = null)
    {
        var sourceResource = new V1Secret
        {
            ApiVersion = V1Secret.KubeApiVersion,
            Kind = V1Secret.KubeKind,
            Metadata = new V1ObjectMeta
            {
                Name = name ?? Guid.CreateVersion7().ToString(),
                NamespaceProperty = namespaceName ?? Guid.CreateVersion7().ToString(),
                Annotations = annotations ?? new ReflectorAnnotationsBuilder().Build()
            },
            StringData = data ?? new Dictionary<string, string>
            {
                { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }
            },
            Type = "Opaque"
        };

        var namespaces = await client.CoreV1.ListNamespaceAsync();
        var nsExists = namespaces.Items.Any(s => s.Name() == namespaceName);
        if (!nsExists)
            await CreateNamespaceAsync(sourceResource.Metadata.NamespaceProperty);
        sourceResource = await client.CoreV1
            .CreateNamespacedSecretAsync(sourceResource, sourceResource.Metadata.NamespaceProperty);
        return sourceResource;
    }

    private async Task DeleteResource(IKubernetes client, string name, string namespaceName,
        CancellationToken cancellationToken = default)
    {
        await client.CoreV1.DeleteNamespacedSecretAsync(name, namespaceName, cancellationToken: cancellationToken);
    }


    private async Task<bool> WaitForResource(IKubernetes client, string name, string namespaceName,
        CancellationToken cancellationToken = default)
    {
        return await ResourceExistsResiliencePipeline.ExecuteAsync(async token =>
        {
            var resource = await client.CoreV1.ReadNamespacedSecretAsync(
                name, namespaceName, cancellationToken: token);
            return resource is not null;
        }, cancellationToken);
    }


    private async Task<bool> ResourceExists(IKubernetes client, string name, string namespaceName,
        CancellationToken cancellationToken = default)
    {
        bool exists;
        try
        {
            await client.CoreV1.ReadNamespacedSecretAsync(name, namespaceName,
                cancellationToken: TestContext.Current.CancellationToken);
            exists = true;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }

        return exists;
    }
}