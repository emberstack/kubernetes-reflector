using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Core;

public class SecretMirror : ResourceMirror<V1Secret>
{
    public SecretMirror(ILogger<SecretMirror> logger, IKubernetes client) : base(logger, client)
    {
    }

    protected override async Task<V1Secret[]> OnResourceWithNameList(string itemRefName)
    {
        return (await Client.ListSecretForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.PatchNamespacedSecretWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1Secret source, JsonPatchDocument<V1Secret> patchDoc)
    {
        patchDoc.Replace(e => e.Data, source.Data);
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1Secret item, string ns)
    {
        return Client.CreateNamespacedSecretAsync(item, ns);
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

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.DeleteNamespacedSecretAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1Secret> OnResourceGet(KubeRef refId)
    {
        return Client.ReadNamespacedSecretAsync(refId.Name, refId.Namespace);
    }

    protected override Task<bool> OnResourceIgnoreCheck(V1Secret item)
    {
        //Skip helm version secrets. This can cause a terrible amount of traffic.
        var ignore = item.Type.StartsWith("helm.sh");
        return Task.FromResult(ignore);
    }
}