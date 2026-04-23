using System.Text.RegularExpressions;

namespace ES.Kubernetes.Reflector.Watchers.Core;

internal static class GlobMatcher
{
    internal static Regex[] ParseGlobPatterns(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns)) return [];

        return
        [
            .. patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new Regex(
                    "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    RegexOptions.Compiled))
        ];
    }

    internal static bool IsNamespaceExcluded(string? ns, Regex[] patterns)
    {
        if (patterns.Length == 0 || string.IsNullOrEmpty(ns)) return false;
        return patterns.Any(p => p.IsMatch(ns));
    }
}
