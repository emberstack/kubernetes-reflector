using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Core;

public class ServiceAccountMirror : ResourceMirror<V1ServiceAccount>
{
    public ServiceAccountMirror(ILogger<ServiceAccountMirror> logger, IKubernetes client) : base(logger, client)
    {
    }

    protected override async Task<V1ServiceAccount[]> OnResourceWithNameList(string itemRefName)
    {
        return (await Client.CoreV1.ListServiceAccountForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.CoreV1.PatchNamespacedServiceAccountWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1ServiceAccount source, JsonPatchDocument<V1ServiceAccount> patchDoc)
    {
        // Just update annotations.
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1ServiceAccount item, string ns)
    {
        item.Metadata.ResourceVersion = null;
        return Client.CoreV1.CreateNamespacedServiceAccountAsync(item, ns);
    }

    protected override Task<V1ServiceAccount> OnResourceClone(V1ServiceAccount sourceResource)
    {
        return Task.FromResult(new V1ServiceAccount
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.CoreV1.DeleteNamespacedServiceAccountAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1ServiceAccount> OnResourceGet(KubeRef refId)
    {
        return Client.CoreV1.ReadNamespacedServiceAccountAsync(refId.Name, refId.Namespace);
    }
}