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

            AllowedNamespacesSelector = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AllowedNamespacesSelector,
                    out string? allowedNamespacesSelector)
                ? allowedNamespacesSelector ?? string.Empty
                : string.Empty,

            AutoEnabled = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AutoEnabled, out bool autoEnabled) && autoEnabled,

            AutoNamespaces = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AutoNamespaces, out string? autoNamespaces)
                ? autoNamespaces ?? string.Empty
                : string.Empty,

            AutoNamespacesSelector = metadata
                .TryGetAnnotationValue(Annotations.Reflection.AutoNamespacesSelector,
                    out string? autoNamespacesSelector)
                ? autoNamespacesSelector ?? string.Empty
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

    /// <summary>
    ///     Checks if the source properties allow reflection to the given namespace (by name only).
    ///     Use the overload accepting V1Namespace for label selector support.
    /// </summary>
    public static bool CanBeReflectedToNamespace(this MirroringProperties properties, string ns) =>
        properties.Allowed && PatternListMatch(properties.AllowedNamespaces, ns);

    /// <summary>
    ///     Checks if the source properties allow reflection to the given namespace,
    ///     including label selector matching when a V1Namespace object is available.
    /// </summary>
    public static bool CanBeReflectedToNamespace(this MirroringProperties properties, V1Namespace ns) =>
        properties.Allowed && MatchNamespace(properties.AllowedNamespaces, properties.AllowedNamespacesSelector, ns);

    /// <summary>
    ///     Checks if the source properties allow auto-reflection to the given namespace (by name only).
    ///     Use the overload accepting V1Namespace for label selector support.
    /// </summary>
    public static bool CanBeAutoReflectedToNamespace(this MirroringProperties properties, string ns) =>
        properties.CanBeReflectedToNamespace(ns) && properties.AutoEnabled &&
        PatternListMatch(properties.AutoNamespaces, ns);

    /// <summary>
    ///     Checks if the source properties allow auto-reflection to the given namespace,
    ///     including label selector matching when a V1Namespace object is available.
    /// </summary>
    public static bool CanBeAutoReflectedToNamespace(this MirroringProperties properties, V1Namespace ns) =>
        properties.CanBeReflectedToNamespace(ns) && properties.AutoEnabled &&
        MatchNamespace(properties.AutoNamespaces, properties.AutoNamespacesSelector, ns);

    /// <summary>
    ///     Returns true if the namespace matches either the name pattern list or the label selector (OR logic).
    ///     If both are empty, returns true (allow all).
    /// </summary>
    private static bool MatchNamespace(string patternList, string labelSelector, V1Namespace ns)
    {
        var hasPatterns = !string.IsNullOrEmpty(patternList);
        var hasSelector = !string.IsNullOrEmpty(labelSelector);

        // If neither is set, allow all (same as existing behavior)
        if (!hasPatterns && !hasSelector) return true;

        // OR logic: match if either the name pattern or label selector matches
        if (hasPatterns && PatternListMatch(patternList, ns.Name())) return true;
        if (hasSelector && LabelSelectorMatch(labelSelector, ns)) return true;

        return false;
    }

    private static bool PatternListMatch(string patternList, string value)
    {
        if (string.IsNullOrEmpty(patternList)) return true;
        var regexPatterns = patternList.Split([","], StringSplitOptions.RemoveEmptyEntries);
        return regexPatterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
            .Select(pattern => Regex.Match(value, pattern))
            .Any(match => match.Success && match.Value.Length == value.Length);
    }

    /// <summary>
    ///     Matches a Kubernetes label selector string against namespace labels.
    ///     Supports equality-based (=, ==, !=) and existence-based (key, !key) selectors.
    ///     Multiple selectors separated by commas are ANDed together.
    /// </summary>
    internal static bool LabelSelectorMatch(string selector, V1Namespace ns)
    {
        if (string.IsNullOrWhiteSpace(selector)) return true;

        var labels = ns.Metadata?.Labels ?? new Dictionary<string, string>();
        var requirements = SplitRequirements(selector);

        foreach (var raw in requirements)
        {
            var requirement = raw.Trim();
            if (string.IsNullOrEmpty(requirement)) continue;

            // Handle set-based: key in (v1,v2) / key notin (v1,v2)
            if (TryParseSetBased(requirement, labels, out var setResult))
            {
                if (!setResult) return false;
                continue;
            }

            // Handle != (must check before =)
            if (requirement.Contains("!="))
            {
                var parts = requirement.Split("!=", 2);
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (labels.TryGetValue(key, out var labelValue) && labelValue == value) return false;
                continue;
            }

            // Handle == or =
            var eqIndex = requirement.IndexOf("==", StringComparison.Ordinal);
            if (eqIndex < 0) eqIndex = requirement.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = requirement[..eqIndex].TrimEnd('=').Trim();
                var value = requirement[(eqIndex + (requirement[eqIndex..].StartsWith("==") ? 2 : 1))..].Trim();
                if (!labels.TryGetValue(key, out var labelValue) || labelValue != value) return false;
                continue;
            }

            // Handle !key (not exists)
            if (requirement.StartsWith('!'))
            {
                var key = requirement[1..].Trim();
                if (labels.ContainsKey(key)) return false;
                continue;
            }

            // Handle key (exists)
            if (!labels.ContainsKey(requirement)) return false;
        }

        return true;
    }

    private static List<string> SplitRequirements(string selector)
    {
        var results = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < selector.Length; i++)
        {
            switch (selector[i])
            {
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0:
                    var part = selector[start..i].Trim();
                    if (part.Length > 0) results.Add(part);
                    start = i + 1;
                    break;
            }
        }
        var last = selector[start..].Trim();
        if (last.Length > 0) results.Add(last);
        return results;
    }

    private static bool TryParseSetBased(string requirement, IDictionary<string, string> labels, out bool result)
    {
        result = false;

        // Match "key in (v1,v2)" or "key notin (v1,v2)"
        var match = Regex.Match(requirement, @"^([a-zA-Z0-9_./-]+)\s+(in|notin)\s+\(([^)]*)\)$");
        if (!match.Success) return false;

        var key = match.Groups[1].Value;
        var op = match.Groups[2].Value;
        var values = match.Groups[3].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .ToHashSet();

        labels.TryGetValue(key, out var labelValue);

        result = op switch
        {
            "in" => labelValue != null && values.Contains(labelValue),
            "notin" => labelValue == null || !values.Contains(labelValue),
            _ => false
        };

        return true;
    }
}
