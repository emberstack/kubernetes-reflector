using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Mirroring.Extensions;
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
        return (await Client.CoreV1.ListSecretForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.CoreV1.PatchNamespacedSecretWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1Secret source, JsonPatchDocument<V1Secret> patchDoc, Dictionary<string, string> mapping)
    {
        patchDoc.Replace(e => e.Data, MappedData(source.Data, mapping));
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1Secret item, string ns)
    {
        return Client.CoreV1.CreateNamespacedSecretAsync(item, ns);
    }

    protected override Task<V1Secret> OnResourceClone(V1Secret sourceResource, Dictionary<string, string> mapping) 
    {
        return Task.FromResult(new V1Secret
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Type = sourceResource.Type,
            Data = MappedData(sourceResource.Data, mapping)
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.CoreV1.DeleteNamespacedSecretAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1Secret> OnResourceGet(KubeRef refId)
    {
        return Client.CoreV1.ReadNamespacedSecretAsync(refId.Name, refId.Namespace);
    }

    protected override Task<bool> OnResourceIgnoreCheck(V1Secret item)
    {
        //Skip helm version secrets. This can cause a terrible amount of traffic.
        var ignore = item.Type.StartsWith("helm.sh");
        return Task.FromResult(ignore);
    }
}