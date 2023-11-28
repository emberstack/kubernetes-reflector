namespace ES.Kubernetes.Reflector.Core.Configuration;

public class ReflectorOptions
{
    public WatcherOptions? Watcher { get; init; }
    public KubernetesOptions? Kubernetes { get; init; }
}