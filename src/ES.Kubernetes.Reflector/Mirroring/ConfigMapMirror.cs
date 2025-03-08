using ES.FX.KubernetesClient.Models;
using ES.Kubernetes.Reflector.Mirroring.Core;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Mirroring;

public class ConfigMapMirror(ILogger<ConfigMapMirror> logger, IKubernetes kubernetes)
    : ResourceMirror<V1ConfigMap>(logger, kubernetes)
{
    protected override async Task<V1ConfigMap[]> OnResourceWithNameList(string itemRefName) =>
    [
        .. (await Kubernetes.CoreV1.ListConfigMapForAllNamespacesAsync(
            fieldSelector: $"metadata.name={itemRefName}"))
        .Items
    ];

    protected override async Task OnResourceApplyPatch(V1Patch patch, NamespacedName refId)
    {
        await Kubernetes.CoreV1.PatchNamespacedConfigMapAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1ConfigMap source, JsonPatchDocument<V1ConfigMap> patchDoc)
    {
        patchDoc.Replace(e => e.Data, source.Data);
        patchDoc.Replace(e => e.BinaryData, source.BinaryData);
        return Task.CompletedTask;
    }

    protected override async Task OnResourceCreate(V1ConfigMap item, string ns)
    {
        await Kubernetes.CoreV1.CreateNamespacedConfigMapAsync(item, ns);
    }

    protected override Task<V1ConfigMap> OnResourceClone(V1ConfigMap sourceResource) =>
        Task.FromResult(new V1ConfigMap
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Data = sourceResource.Data,
            BinaryData = sourceResource.BinaryData
        });

    protected override async Task OnResourceDelete(NamespacedName resourceId)
    {
        await Kubernetes.CoreV1.DeleteNamespacedConfigMapAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override async Task<V1ConfigMap> OnResourceGet(NamespacedName refId) =>
        await Kubernetes.CoreV1.ReadNamespacedConfigMapAsync(refId.Name, refId.Namespace);
}