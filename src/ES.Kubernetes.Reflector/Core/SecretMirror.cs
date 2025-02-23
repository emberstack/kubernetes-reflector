using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Core;

public class SecretMirror(ILogger<SecretMirror> logger, IServiceProvider serviceProvider)
    : ResourceMirror<V1Secret>(logger, serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    protected override async Task<V1Secret[]> OnResourceWithNameList(string itemRefName)
    {
        using var client = _serviceProvider.GetRequiredService<IKubernetes>();
        return (await client.CoreV1.ListSecretForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}"))
            .Items
            .ToArray();
    }

    protected override async Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        using var client = _serviceProvider.GetRequiredService<IKubernetes>();
        await client.CoreV1.PatchNamespacedSecretWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1Secret source, JsonPatchDocument<V1Secret> patchDoc)
    {
        patchDoc.Replace(e => e.Data, source.Data);
        return Task.CompletedTask;
    }

    protected override async Task OnResourceCreate(V1Secret item, string ns)
    {
        using var client = _serviceProvider.GetRequiredService<IKubernetes>();
        await client.CoreV1.CreateNamespacedSecretAsync(item, ns);
    }

    protected override Task<V1Secret> OnResourceClone(V1Secret sourceResource)
    {
        return Task.FromResult(new V1Secret
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Type = sourceResource.Type,
            Data = sourceResource.Data
        });
    }

    protected override async Task OnResourceDelete(KubeRef resourceId)
    {
        using var client = _serviceProvider.GetRequiredService<IKubernetes>();
        await client.CoreV1.DeleteNamespacedSecretAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override async Task<V1Secret> OnResourceGet(KubeRef refId)
    {
        using var client = _serviceProvider.GetRequiredService<IKubernetes>();
        return await client.CoreV1.ReadNamespacedSecretAsync(refId.Name, refId.Namespace);
    }

    protected override Task<bool> OnResourceIgnoreCheck(V1Secret item)
    {
        //Skip helm version secrets. This can cause a terrible amount of traffic.
        var ignore = item.Type.StartsWith("helm.sh");
        return Task.FromResult(ignore);
    }
}