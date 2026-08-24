namespace ES.Kubernetes.Reflector.Configuration;

public class WatcherOptions
{
    public int? Timeout { get; set; }

    public int? RetryDelay { get; set; }

    public int? RetryDelayMax { get; set; }

    /// <summary>
    ///     Comma-separated list of namespace patterns to exclude from reflection processing.
    ///     Supports glob wildcards: * (any characters), ? (single character).
    ///     Example: "ephie-*,kube-system,*-temp"
    /// </summary>
    public string? ExcludedNamespaces { get; set; }
}