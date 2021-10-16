using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Core;

public class ConfigMapMirror : ResourceMirror<V1ConfigMap>
{
    public ConfigMapMirror(ILogger<ConfigMapMirror> logger, IKubernetes client) : base(logger, client)
    {
    }

    protected override async Task<V1ConfigMap[]> OnResourceWithNameList(string itemRefName)
    {
        return (await Client.ListConfigMapForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.PatchNamespacedConfigMapAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1ConfigMap source, JsonPatchDocument<V1ConfigMap> patchDoc)
    {
        patchDoc.Replace(e => e.Data, source.Data);
        patchDoc.Replace(e => e.BinaryData, source.BinaryData);
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1ConfigMap item, string ns)
    {
        return Client.CreateNamespacedConfigMapAsync(item, ns);
    }

    protected override Task<V1ConfigMap> OnResourceClone(V1ConfigMap sourceResource)
    {
        return Task.FromResult(new V1ConfigMap
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Data = sourceResource.Data,
            BinaryData = sourceResource.BinaryData
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.DeleteNamespacedConfigMapAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1ConfigMap> OnResourceGet(KubeRef refId)
    {
        return Client.ReadNamespacedConfigMapAsync(refId.Name, refId.Namespace);
    }
}