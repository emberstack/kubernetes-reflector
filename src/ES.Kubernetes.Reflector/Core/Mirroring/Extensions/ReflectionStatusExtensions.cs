using System.Text.RegularExpressions;

namespace ES.Kubernetes.Reflector.Core.Mirroring.Extensions;

public static class ReflectionStatusExtensions
{
    public static bool CanBeReflectedToNamespace(this ReflectorProperties status, string ns)
    {
        return status.Allowed && PatternListMatch(status.AllowedNamespaces, ns);
    }


    public static bool CanBeAutoReflectedToNamespace(this ReflectorProperties status, string ns)
    {
        return status.CanBeReflectedToNamespace(ns) && status.AutoEnabled &&
               PatternListMatch(status.AutoNamespaces, ns);
    }


    private static bool PatternListMatch(string patternList, string value)
    {
        if (string.IsNullOrEmpty(patternList)) return true;
        var regexPatterns = patternList.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries);
        return regexPatterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
            .Select(pattern => Regex.Match(value, pattern))
            .Any(match => match.Success && match.Value.Length == value.Length);
    }
}