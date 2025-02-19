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
        return (await Client.CoreV1.ListConfigMapForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.CoreV1.PatchNamespacedConfigMapAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1ConfigMap source, JsonPatchDocument<V1ConfigMap> patchDoc, Dictionary<string, string>? mapping)
    {
        patchDoc.Replace(e => e.Data, MappedData(source.Data, mapping));
        patchDoc.Replace(e => e.BinaryData, MappedData(source.BinaryData, mapping));
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1ConfigMap item, string ns)
    {
        return Client.CoreV1.CreateNamespacedConfigMapAsync(item, ns);
    }

    protected override Task<V1ConfigMap> OnResourceClone(V1ConfigMap sourceResource, Dictionary<string, string>? mapping)
    {
        return Task.FromResult(new V1ConfigMap
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Data =  MappedData(sourceResource.Data, mapping),
            BinaryData = MappedData(sourceResource.BinaryData, mapping)
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.CoreV1.DeleteNamespacedConfigMapAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1ConfigMap> OnResourceGet(KubeRef refId)
    {
        return Client.CoreV1.ReadNamespacedConfigMapAsync(refId.Name, refId.Namespace);
    }
}