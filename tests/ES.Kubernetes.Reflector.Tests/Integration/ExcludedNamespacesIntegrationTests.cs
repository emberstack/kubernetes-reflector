using System.Net;
using ES.Kubernetes.Reflector.Tests.Additions;
using ES.Kubernetes.Reflector.Tests.Fixtures;
using ES.Kubernetes.Reflector.Tests.Integration.Base;
using ES.Kubernetes.Reflector.Tests.Integration.Fixtures;
using JetBrains.Annotations;
using k8s;
using k8s.Autorest;
using k8s.Models;

[assembly: AssemblyFixture(typeof(ExcludedNamespacesIntegrationFixture))]

namespace ES.Kubernetes.Reflector.Tests.Integration;

[PublicAPI]
public class ExcludedNamespacesIntegrationTests(
    ExcludedNamespacesIntegrationFixture integrationFixture)
    : BaseIntegrationTest(integrationFixture)
{
    [Fact]
    public async Task WatchEvents_FromExcludedNamespace_AreNotReflected()
    {
        var client = await GetKubernetesClient();

        var excludedNamespace = $"excluded-{Guid.CreateVersion7()}";
        var allowedNamespace = $"allowed-{Guid.CreateVersion7()}";
        var targetNamespace = $"target-{Guid.CreateVersion7()}";

        await CreateNamespaceAsync(excludedNamespace);
        await CreateNamespaceAsync(allowedNamespace);
        await CreateNamespaceAsync(targetNamespace);

        // Resource in excluded namespace — should not be mirrored anywhere
        var excludedSourceResource = await CreateResource(client, namespaceName: excludedNamespace,
            annotations: new ReflectorAnnotationsBuilder()
                .WithReflectionAllowed(true)
                .WithAutoEnabled(true).Build());

        // Resource in allowed namespace — should cross-namespace auto-reflect into targetNamespace
        var allowedSourceResource = await CreateResource(client, namespaceName: allowedNamespace,
            annotations: new ReflectorAnnotationsBuilder()
                .WithReflectionAllowed(true)
                .WithAllowedNamespaces("^target-.*")
                .WithAutoEnabled(true).Build());

        await DelayForReflection();

        // The excluded-namespace resource should not have triggered auto-reflection into any other namespace
        Assert.False(await ResourceExists(client,
            excludedSourceResource.Name(), targetNamespace,
            TestContext.Current.CancellationToken));

        // The allowed-namespace resource should cross-namespace reflect into targetNamespace
        Assert.True(await WaitForResource(client,
            allowedSourceResource.Name(), targetNamespace,
            TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task AutoReflect_IntoExcludedTargetNamespace_StillReflects()
    {
        // The exclusion filter drops watch events FROM excluded namespaces (source-side filtering
        // via item.Metadata.NamespaceProperty). Namespace objects are cluster-scoped so their
        // events are never filtered, meaning excluded namespaces stay in the namespace cache and
        // remain valid auto-reflection targets. This test pins down that behavior.
        var client = await GetKubernetesClient();

        var sourceNamespace = $"allowed-{Guid.CreateVersion7()}";
        var excludedTargetNamespace = $"excluded-{Guid.CreateVersion7()}";

        await CreateNamespaceAsync(sourceNamespace);
        await CreateNamespaceAsync(excludedTargetNamespace);

        var sourceResource = await CreateResource(client, namespaceName: sourceNamespace,
            annotations: new ReflectorAnnotationsBuilder()
                .WithReflectionAllowed(true)
                .WithAutoEnabled(true).Build());

        await DelayForReflection();

        Assert.True(await WaitForResource(client,
            sourceResource.Name(), excludedTargetNamespace,
            TestContext.Current.CancellationToken));
    }

    private async Task<V1Secret> CreateResource(IKubernetes client,
        string? name = null, string? namespaceName = null,
        Dictionary<string, string>? annotations = null)
    {
        namespaceName ??= Guid.CreateVersion7().ToString();
        var sourceResource = new V1Secret
        {
            ApiVersion = V1Secret.KubeApiVersion,
            Kind = V1Secret.KubeKind,
            Metadata = new V1ObjectMeta
            {
                Name = name ?? Guid.CreateVersion7().ToString(),
                NamespaceProperty = namespaceName,
                Annotations = annotations ?? new ReflectorAnnotationsBuilder().Build()
            },
            StringData = new Dictionary<string, string>
            {
                { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }
            },
            Type = "Opaque"
        };

        var namespaces = await client.CoreV1.ListNamespaceAsync();
        if (!namespaces.Items.Any(s => s.Name() == namespaceName))
            await CreateNamespaceAsync(namespaceName);

        return await client.CoreV1.CreateNamespacedSecretAsync(sourceResource, namespaceName);
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
        try
        {
            await client.CoreV1.ReadNamespacedSecretAsync(name, namespaceName,
                cancellationToken: TestContext.Current.CancellationToken);
            return true;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
