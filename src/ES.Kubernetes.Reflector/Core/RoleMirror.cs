using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using System.Text.Json;

namespace ES.Kubernetes.Reflector.Core;

public class RoleMirror : ResourceMirror<V1Role>
{
    public RoleMirror(ILogger<RoleMirror> logger, IKubernetes client) : base(logger, client)
    {
    }

    protected override async Task<V1Role[]> OnResourceWithNameList(string itemRefName)
    {
        return (await Client.RbacAuthorizationV1.ListRoleForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.RbacAuthorizationV1.PatchNamespacedRoleWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1Role source, JsonPatchDocument<V1Role> patchDoc)
    {
        // Replace with new List of Rules
        patchDoc.Replace(e => e.Rules, source.Rules);
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1Role item, string ns)
    {
        item.Metadata.ResourceVersion = null;
        return Client.RbacAuthorizationV1.CreateNamespacedRoleAsync(item, ns);
    }

    protected override Task<V1Role> OnResourceClone(V1Role sourceResource)
    {
        return Task.FromResult(new V1Role
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Rules = sourceResource.Rules
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.RbacAuthorizationV1.DeleteNamespacedRoleAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1Role> OnResourceGet(KubeRef refId)
    {
        return Client.RbacAuthorizationV1.ReadNamespacedRoleAsync(refId.Name, refId.Namespace);
    }

}