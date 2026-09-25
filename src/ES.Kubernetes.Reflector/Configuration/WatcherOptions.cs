namespace ES.Kubernetes.Reflector.Configuration;

public class WatcherOptions
{
    public int? Timeout { get; set; }

    /// <summary>
    ///     Maximum time in seconds a single watcher event may spend in its handlers before it is
    ///     canceled and skipped, so a hung Kubernetes API call cannot stall event processing.
    /// </summary>
    public int? EventHandlerTimeout { get; set; }

    /// <summary>
    ///     Comma-separated list of namespace patterns to exclude from reflection processing.
    ///     Supports glob wildcards: * (any characters), ? (single character).
    ///     Example: "ephie-*,kube-system,*-temp"
    /// </summary>
    public string? ExcludedNamespaces { get; set; }
}