using System.Text.RegularExpressions;
using ES.FX.Additions.KubernetesClient.Models;
using ES.FX.Additions.KubernetesClient.Models.Extensions;
using k8s;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Mirroring.Core;

public static class MirroringPropertiesExtensions
{
    // Kubernetes label name: 1-63 chars, alphanumeric plus _ . -, must start/end alphanumeric.
    private static readonly Regex LabelNameRegex = new(
        @"^[A-Za-z0-9]([A-Za-z0-9._-]{0,61}[A-Za-z0-9])?$",
        RegexOptions.Compiled);

    // Kubernetes label key prefix: DNS subdomain (lowercase alphanumeric and -, period-separated labels).
    private static readonly Regex LabelPrefixRegex = new(
        @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$",
        RegexOptions.Compiled);

    // Kubernetes label value: up to 63 chars, same character rules as the name (empty values are allowed).
    private static readonly Regex LabelValueRegex = new(
        @"^([A-Za-z0-9]([A-Za-z0-9._-]{0,61}[A-Za-z0-9])?)?$",
        RegexOptions.Compiled);

    // Matches "<key> in (<values>)" or "<key> notin (<values>)" at the requirement level.
    private static readonly Regex SetBasedRequirementRegex = new(
        @"^(?<key>\S+)\s+(?<op>in|notin)\s*\((?<values>[^)]*)\)$",
        RegexOptions.Compiled);

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
    ///     Parses the label selector annotations on this properties instance and returns a list of parse errors,
    ///     one per malformed selector. Returns an empty list if all selectors are valid (or unset). Callers can
    ///     surface these errors so operators get feedback about misconfigured annotations.
    /// </summary>
    public static IReadOnlyList<string> GetLabelSelectorErrors(this MirroringProperties properties)
    {
        var errors = new List<string>();
        CollectLabelSelectorErrors(
            Annotations.Reflection.AllowedNamespacesSelector,
            properties.AllowedNamespacesSelector, errors);
        CollectLabelSelectorErrors(
            Annotations.Reflection.AutoNamespacesSelector,
            properties.AutoNamespacesSelector, errors);
        return errors;
    }

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
    ///     Supports equality-based (=, ==, !=), set-based (in, notin), and existence-based (key, !key) selectors.
    ///     Multiple selectors separated by commas are ANDed together. Invalid selectors fail closed (return false).
    /// </summary>
    internal static bool LabelSelectorMatch(string selector, V1Namespace ns)
    {
        if (string.IsNullOrWhiteSpace(selector)) return true;

        if (!TryParseLabelSelector(selector, out var parsed, out _))
            return false;

        var labels = ns.Metadata?.Labels ?? new Dictionary<string, string>();
        return MatchesLabels(parsed, labels);
    }

    /// <summary>
    ///     Parses a Kubernetes label selector string into a <see cref="V1LabelSelector" />. Returns false if the
    ///     selector is malformed; <paramref name="errors" /> lists the reasons. An empty or whitespace selector
    ///     parses successfully into an empty selector (matches everything).
    /// </summary>
    internal static bool TryParseLabelSelector(string raw, out V1LabelSelector selector,
        out IReadOnlyList<string> errors)
    {
        selector = new V1LabelSelector();
        var errorList = new List<string>();
        errors = errorList;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        var requirements = SplitRequirements(raw);
        if (requirements.Count == 0)
        {
            errorList.Add("selector is not empty but contains no requirements");
            return false;
        }

        var matchLabels = new Dictionary<string, string>();
        var matchExpressions = new List<V1LabelSelectorRequirement>();

        foreach (var requirement in requirements)
        {
            if (TryParseSetBased(requirement, out var setRequirement, out var setError))
            {
                if (setError != null) errorList.Add(setError);
                else matchExpressions.Add(setRequirement!);
                continue;
            }

            if (TryParseInequality(requirement, errorList, out var neqRequirement))
            {
                if (neqRequirement != null) matchExpressions.Add(neqRequirement);
                continue;
            }

            if (TryParseEquality(requirement, errorList, matchLabels)) continue;

            if (TryParseExistence(requirement, errorList, out var existRequirement))
            {
                if (existRequirement != null) matchExpressions.Add(existRequirement);
                continue;
            }

            errorList.Add($"requirement '{requirement}' could not be parsed");
        }

        if (errorList.Count > 0) return false;

        if (matchLabels.Count > 0) selector.MatchLabels = matchLabels;
        if (matchExpressions.Count > 0) selector.MatchExpressions = matchExpressions;
        return true;
    }

    /// <summary>
    ///     Evaluates a <see cref="V1LabelSelector" /> against a label dictionary using the standard Kubernetes
    ///     semantics (MatchLabels are ANDed; MatchExpressions are ANDed).
    /// </summary>
    internal static bool MatchesLabels(V1LabelSelector selector, IDictionary<string, string> labels)
    {
        if (selector.MatchLabels != null)
            foreach (var kv in selector.MatchLabels)
                if (!labels.TryGetValue(kv.Key, out var labelValue) || labelValue != kv.Value)
                    return false;

        if (selector.MatchExpressions == null) return true;

        foreach (var expression in selector.MatchExpressions)
        {
            var hasLabel = labels.TryGetValue(expression.Key, out var labelValue);
            switch (expression.OperatorProperty)
            {
                case "In":
                    if (!hasLabel || expression.Values == null ||
                        !expression.Values.Contains(labelValue!)) return false;
                    break;
                case "NotIn":
                    if (hasLabel && expression.Values != null &&
                        expression.Values.Contains(labelValue!)) return false;
                    break;
                case "Exists":
                    if (!hasLabel) return false;
                    break;
                case "DoesNotExist":
                    if (hasLabel) return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static void CollectLabelSelectorErrors(string annotation, string value, List<string> destination)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (TryParseLabelSelector(value, out _, out var parseErrors)) return;
        foreach (var error in parseErrors)
            destination.Add($"{annotation} '{value}': {error}");
    }

    private static List<string> SplitRequirements(string selector)
    {
        var results = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < selector.Length; i++)
            switch (selector[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    var part = selector[start..i].Trim();
                    if (part.Length > 0) results.Add(part);
                    start = i + 1;
                    break;
            }

        var last = selector[start..].Trim();
        if (last.Length > 0) results.Add(last);
        return results;
    }

    private static bool TryParseSetBased(string requirement, out V1LabelSelectorRequirement? result,
        out string? error)
    {
        result = null;
        error = null;

        var match = SetBasedRequirementRegex.Match(requirement);
        if (!match.Success) return false;

        var key = match.Groups["key"].Value;
        var op = match.Groups["op"].Value;
        var values = match.Groups["values"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .ToList();

        if (!IsValidLabelKey(key))
        {
            error = $"invalid label key '{key}' in set-based requirement";
            return true;
        }

        if (values.Count == 0)
        {
            error = $"set-based requirement for key '{key}' has no values";
            return true;
        }

        foreach (var value in values)
            if (!IsValidLabelValue(value))
            {
                error = $"invalid label value '{value}' for key '{key}'";
                return true;
            }

        result = new V1LabelSelectorRequirement
        {
            Key = key,
            OperatorProperty = op == "in" ? "In" : "NotIn",
            Values = values
        };
        return true;
    }

    private static bool TryParseInequality(string requirement, List<string> errors,
        out V1LabelSelectorRequirement? result)
    {
        result = null;
        if (!requirement.Contains("!=")) return false;

        var parts = requirement.Split("!=", 2);
        var key = parts[0].Trim();
        var value = parts[1].Trim();

        if (!IsValidLabelKey(key))
        {
            errors.Add($"invalid label key '{key}' in inequality requirement");
            return true;
        }

        if (!IsValidLabelValue(value))
        {
            errors.Add($"invalid label value '{value}' for key '{key}'");
            return true;
        }

        result = new V1LabelSelectorRequirement
        {
            Key = key,
            OperatorProperty = "NotIn",
            Values = new List<string> { value }
        };
        return true;
    }

    private static bool TryParseEquality(string requirement, List<string> errors,
        Dictionary<string, string> matchLabels)
    {
        var doubleEq = requirement.IndexOf("==", StringComparison.Ordinal);
        var singleEq = requirement.IndexOf('=');
        if (doubleEq < 0 && singleEq < 0) return false;

        var eqIndex = doubleEq >= 0 ? doubleEq : singleEq;
        var opLength = doubleEq >= 0 ? 2 : 1;
        var key = requirement[..eqIndex].Trim();
        var value = requirement[(eqIndex + opLength)..].Trim();

        if (!IsValidLabelKey(key))
        {
            errors.Add($"invalid label key '{key}' in equality requirement");
            return true;
        }

        if (!IsValidLabelValue(value))
        {
            errors.Add($"invalid label value '{value}' for key '{key}'");
            return true;
        }

        matchLabels[key] = value;
        return true;
    }

    private static bool TryParseExistence(string requirement, List<string> errors,
        out V1LabelSelectorRequirement? result)
    {
        result = null;
        var negated = requirement.StartsWith('!');
        var key = negated ? requirement[1..].Trim() : requirement;

        if (!IsValidLabelKey(key))
        {
            errors.Add($"invalid label key '{key}' in existence requirement");
            return true;
        }

        result = new V1LabelSelectorRequirement
        {
            Key = key,
            OperatorProperty = negated ? "DoesNotExist" : "Exists"
        };
        return true;
    }

    private static bool IsValidLabelKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        var slashIndex = key.IndexOf('/');
        string name;
        if (slashIndex >= 0)
        {
            var prefix = key[..slashIndex];
            if (prefix.Length == 0 || prefix.Length > 253) return false;
            if (!LabelPrefixRegex.IsMatch(prefix)) return false;
            name = key[(slashIndex + 1)..];
        }
        else
        {
            name = key;
        }

        if (name.Length == 0 || name.Length > 63) return false;
        return LabelNameRegex.IsMatch(name);
    }

    private static bool IsValidLabelValue(string value)
    {
        if (value.Length > 63) return false;
        return LabelValueRegex.IsMatch(value);
    }
}
