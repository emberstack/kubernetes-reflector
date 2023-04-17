using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Core;

public class NetworkPolicyMirror : ResourceMirror<V1NetworkPolicy>
{
    public NetworkPolicyMirror(ILogger<NetworkPolicyMirror> logger, IKubernetes client) : base(logger, client)
    {
    }

    protected override async Task<V1NetworkPolicy[]> OnResourceWithNameList(string itemRefName)
    {
        return (await Client.NetworkingV1.ListNetworkPolicyForAllNamespacesAsync(fieldSelector: $"metadata.name={itemRefName}")).Items
            .ToArray();
    }

    protected override Task OnResourceApplyPatch(V1Patch patch, KubeRef refId)
    {
        return Client.NetworkingV1.PatchNamespacedNetworkPolicyAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1NetworkPolicy source, JsonPatchDocument<V1NetworkPolicy> patchDoc)
    {
        patchDoc.Replace(e => e.Spec, source.Spec);
        return Task.CompletedTask;
    }

    protected override Task OnResourceCreate(V1NetworkPolicy item, string ns)
    {
        return Client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(item, ns);
    }

    protected override Task<V1NetworkPolicy> OnResourceClone(V1NetworkPolicy sourceResource)
    {
        return Task.FromResult(new V1NetworkPolicy
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Spec = sourceResource.Spec
        });
    }

    protected override Task OnResourceDelete(KubeRef resourceId)
    {
        return Client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override Task<V1NetworkPolicy> OnResourceGet(KubeRef refId)
    {
        return Client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(refId.Name, refId.Namespace);
    }
}