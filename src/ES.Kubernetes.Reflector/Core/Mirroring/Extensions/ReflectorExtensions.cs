using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Mirroring.Constants;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Mirroring.Extensions;

public static class ReflectorExtensions
{
    public static ReflectorProperties GetReflectionProperties(this IKubernetesObject<V1ObjectMeta> resource)
    {
        return resource.EnsureMetadata().GetReflectionProperties();
    }

    public static ReflectorProperties GetReflectionProperties(this V1ObjectMeta metadata)
    {
        return new ReflectorProperties
        {
            Version = metadata.ResourceVersion,

            Allowed = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.Allowed, out bool allowed) && allowed,
            AllowedNamespaces = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.AllowedNamespaces, out string? allowedNamespaces)
                ? allowedNamespaces ?? string.Empty
                : string.Empty,
            AutoEnabled = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.AutoEnabled, out bool autoEnabled) && autoEnabled,
            AutoNamespaces = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.AutoNamespaces, out string? autoNamespaces)
                ? autoNamespaces ?? string.Empty
                : string.Empty,
            Reflects = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.Reflects, out string? metaReflects)
                ? string.IsNullOrWhiteSpace(metaReflects) ? KubeRef.Empty :
                KubeRef.TryParse(metaReflects, out var metaReflectsRef) ? metaReflectsRef.Namespace == string.Empty
                    ? new KubeRef(metadata.NamespaceProperty, metaReflectsRef.Name)
                    : metaReflectsRef : KubeRef.Empty
                : KubeRef.Empty,
            IsAutoReflection = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.MetaAutoReflects, out bool metaAutoReflects) && metaAutoReflects,
            ReflectedVersion = metadata.SafeAnnotations()
                .TryGet(Annotations.Reflection.MetaReflectedVersion, out string? reflectedVersion)
                ? string.IsNullOrWhiteSpace(reflectedVersion) ? string.Empty : reflectedVersion
                : string.Empty
        };
    }


    //public static bool FortiReflectionEnabled(this V1ObjectMeta metadata)
    //{
    //    if (Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.FortiEnabled, out var raw) &&
    //        bool.TryParse(raw, out var value))
    //        return value;
    //    return false;
    //}

    //public static string[] FortiReflectionHosts(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.FortiHosts, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
    //        raw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
    //            .Where(s => !string.IsNullOrWhiteSpace(s))
    //            .Select(s => s.Trim()).Distinct().ToArray()
    //        : Array.Empty<string>();
    //}

    //public static string FortiCertificate(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.FortiCertificate, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? null : raw
    //        : null;
    //}

    //#region Ubiquiti

    //public static bool UbiquitiReflectionEnabled(this V1ObjectMeta metadata)
    //{
    //    if (Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.UbiquitiEnabled, out var raw) &&
    //        bool.TryParse(raw, out var value))
    //        return value;
    //    return false;
    //}

    //public static string[] UbiquitiReflectionHosts(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.UbiquitiHosts, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
    //        raw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
    //            .Where(s => !string.IsNullOrWhiteSpace(s))
    //            .Select(s => s.Trim()).Distinct().ToArray()
    //        : Array.Empty<string>();
    //}

    //public static string UbiquitiCertificate(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.UbiquitiCertificate, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? null : raw
    //        : null;
    //}

    //#endregion

    //#region VMware

    //public static bool VMwareReflectionEnabled(this V1ObjectMeta metadata)
    //{
    //    if (Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.VMwareEnabled, out var raw) &&
    //        bool.TryParse(raw, out var value))
    //        return value;
    //    return false;
    //}

    //public static string[] VMwareReflectionHosts(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.VMwareHosts, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
    //        raw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
    //            .Where(s => !string.IsNullOrWhiteSpace(s))
    //            .Select(s => s.Trim()).Distinct().ToArray()
    //        : Array.Empty<string>();
    //}

    //public static string VMwareCertificate(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.VMwareCertificate, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? null : raw
    //        : null;
    //}

    //#endregion

    //#region FreeNAS

    //public static bool FreeNasReflectionEnabled(this V1ObjectMeta metadata)
    //{
    //    if (Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.FreeNasEnabled, out var raw) &&
    //        bool.TryParse(raw, out var value))
    //        return value;
    //    return false;
    //}

    //public static string[] FreeNasReflectionHosts(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.FreeNasHosts, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() :
    //        raw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
    //            .Where(s => !string.IsNullOrWhiteSpace(s))
    //            .Select(s => s.Trim()).Distinct().ToArray()
    //        : Array.Empty<string>();
    //}

    //public static string FreeNasCertificate(this V1ObjectMeta metadata)
    //{
    //    return Common.Extensions.MetadataExtensions.EnsureAnnotations(metadata).TryGetValue(Annotations.Reflection.FreeNasCertificate, out var raw)
    //        ? string.IsNullOrWhiteSpace(raw) ? null : raw
    //        : null;
    //}

    //#endregion
}