using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using System.Text.Json;
using System.Diagnostics;

namespace ES.Kubernetes.Reflector.Core;

public class RoleBindingMirror : ResourceMirror<V1RoleBinding>
{
    public RoleBindingMirror(ILogger<RoleBindingMirror> logger, IKubernetes client) : base(logger, client)
    {
    }

    protected override async Task<V1RoleBinding[]> OnResourceWithNameList(string itemRefName)
    {
        return (await Client.RbacAuthorizationV1.ListRoleBindingForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.RbacAuthorizationV1.PatchNamespacedRoleBindingWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1RoleBinding source, JsonPatchDocument<V1RoleBinding> patchDoc)
    {        
        // Roleref is immutable by design, so we only patch the Subjects list
        patchDoc.Replace(e => e.Subjects, source.Subjects);
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1RoleBinding item, string ns)
    {
        item.Metadata.ResourceVersion = null;
        return Client.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(item, ns);
    }

    protected override Task<V1RoleBinding> OnResourceClone(V1RoleBinding sourceResource)
    {
        return Task.FromResult(new V1RoleBinding
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Subjects = sourceResource.Subjects,
            RoleRef = sourceResource.RoleRef
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.RbacAuthorizationV1.DeleteNamespacedRoleBindingAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1RoleBinding> OnResourceGet(KubeRef refId)
    {
        return Client.RbacAuthorizationV1.ReadNamespacedRoleBindingAsync(refId.Name, refId.Namespace);
    }
}