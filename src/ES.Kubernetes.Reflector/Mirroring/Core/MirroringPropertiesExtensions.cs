using System.Text.RegularExpressions;
using ES.FX.Additions.KubernetesClient.Models;
using ES.FX.Additions.KubernetesClient.Models.Extensions;
using k8s;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Mirroring.Core;

public static class MirroringPropertiesExtensions
{
    public static MirroringProperties GetMirroringProperties(this IKubernetesObject<V1ObjectMeta> resource) =>
        resource.EnsureMetadata().GetMirroringProperties();

    public static MirroringProperties GetMirroringProperties(this V1ObjectMeta metadata) =>
        new()
        {
            ResourceVersion = metadata.ResourceVersion,

            Allowed = metadata
                .TryGetAnnotationValue(Annotations.Reflection.Allowed, out bool allowed) && allowed,

            AllowedNamespaces = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AllowedNamespaces, out string? allowedNamespaces)
                ? allowedNamespaces ?? string.Empty
                : string.Empty,

            AutoEnabled = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AutoEnabled, out bool autoEnabled) && autoEnabled,

            AutoNamespaces = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AutoNamespaces, out string? autoNamespaces)
                ? autoNamespaces ?? string.Empty
                : string.Empty,

            Reflects = metadata
                .TryGetAnnotationValue(Annotations.Reflection.Reflects, out string? metaReflects)
                ? NamespacedName.TryParse(metaReflects, out var id) ? id : null
                : null,

            IsAutoReflection = metadata
                                   .TryGetAnnotationValue(Annotations.Reflection.MetaAutoReflects,
                                       out bool metaAutoReflects) &&
                               metaAutoReflects,

            ReflectedVersion = metadata
                .TryGetAnnotationValue(Annotations.Reflection.MetaReflectedVersion, out string? reflectedVersion)
                ? string.IsNullOrWhiteSpace(reflectedVersion) ? string.Empty : reflectedVersion
                : string.Empty,

            ReflectedAt = metadata
                .TryGetAnnotationValue(Annotations.Reflection.MetaReflectedAt, out string? reflectedAtString)
                ? string.IsNullOrWhiteSpace(reflectedAtString)
                    ? null
                    : DateTimeOffset.Parse(reflectedAtString.Replace("\"", string.Empty))
                : null
        };

    public static bool CanBeReflectedToNamespace(this MirroringProperties properties, string ns) =>
        properties.Allowed && PatternListMatch(properties.AllowedNamespaces, ns);


    public static bool CanBeAutoReflectedToNamespace(this MirroringProperties properties, string ns) =>
        properties.CanBeReflectedToNamespace(ns) && properties.AutoEnabled &&
        PatternListMatch(properties.AutoNamespaces, ns);


    private static bool PatternListMatch(string patternList, string value)
    {
        if (string.IsNullOrEmpty(patternList)) return true;
        var regexPatterns = patternList.Split([","], StringSplitOptions.RemoveEmptyEntries);
        return regexPatterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
            .Select(pattern => Regex.Match(value, pattern))
            .Any(match => match.Success && match.Value.Length == value.Length);
    }
}