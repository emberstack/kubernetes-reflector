namespace ES.Kubernetes.Reflector.Configuration;

public class WatcherOptions
{
    public int? Timeout { get; set; }

    /// <summary>
    ///     Comma-separated list of namespace patterns to exclude from watching.
    ///     Supports glob wildcards: * (any characters), ? (single character).
    ///     Example: "ephie-*,kube-system,*-temp"
    /// </summary>
    public string? ExcludedNamespaces { get; set; }
}