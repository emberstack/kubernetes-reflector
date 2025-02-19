using ES.Kubernetes.Reflector.Core.Resources;

namespace ES.Kubernetes.Reflector.Core.Mirroring;

public class ReflectorProperties
{
    public bool Allowed { get; set; }
    public string AllowedNamespaces { get; set; } = string.Empty;
    public bool AutoEnabled { get; set; }
    public string AutoNamespaces { get; set; } = string.Empty;
    public KubeRef Reflects { get; set; } = KubeRef.Empty;
    public string KeyMapping { get; set; } = string.Empty;
    public string AutoKeyMapping { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;


    public bool IsAutoReflection { get; set; }
    public string ReflectedVersion { get; set; } = string.Empty;
    public DateTimeOffset? ReflectedAt { get; set; }


    public bool IsReflection => !Reflects.Equals(KubeRef.Empty);
}