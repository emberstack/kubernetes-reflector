﻿using ES.FX.KubernetesClient.Models;
using ES.Kubernetes.Reflector.Mirroring.Core;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace ES.Kubernetes.Reflector.Mirroring;

public class SecretMirror(ILogger<SecretMirror> logger, IKubernetes kubernetesClient)
    : ResourceMirror<V1Secret>(logger, kubernetesClient)
{
    protected override async Task<V1Secret[]> OnResourceWithNameList(string itemRefName) =>
    [
        .. (await Kubernetes.CoreV1.ListSecretForAllNamespacesAsync(
            fieldSelector: $"metadata.name={itemRefName}"))
        .Items
    ];

    protected override async Task OnResourceApplyPatch(V1Patch patch, NamespacedName refId)
    {
        await Kubernetes.CoreV1.PatchNamespacedSecretWithHttpMessagesAsync(patch, refId.Name, refId.Namespace);
    }

    protected override Task OnResourceConfigurePatch(V1Secret source, JsonPatchDocument<V1Secret> patchDoc)
    {
        patchDoc.Replace(e => e.Data, source.Data);
        return Task.CompletedTask;
    }

    protected override async Task OnResourceCreate(V1Secret item, string ns)
    {
        await Kubernetes.CoreV1.CreateNamespacedSecretAsync(item, ns);
    }

    protected override Task<V1Secret> OnResourceClone(V1Secret sourceResource) =>
        Task.FromResult(new V1Secret
        {
            ApiVersion = sourceResource.ApiVersion,
            Kind = sourceResource.Kind,
            Type = sourceResource.Type,
            Data = sourceResource.Data
        });

    protected override async Task OnResourceDelete(NamespacedName resourceId)
    {
        await Kubernetes.CoreV1.DeleteNamespacedSecretAsync(resourceId.Name, resourceId.Namespace);
    }

    protected override async Task<V1Secret> OnResourceGet(NamespacedName refId) =>
        await Kubernetes.CoreV1.ReadNamespacedSecretAsync(refId.Name, refId.Namespace);
}

public class SecretEventHandler(SecretMirror mirror) : ResourceEventHandler<V1Secret>(mirror) { }
