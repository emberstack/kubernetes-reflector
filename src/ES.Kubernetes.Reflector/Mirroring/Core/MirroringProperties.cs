using System.Diagnostics.CodeAnalysis;
using ES.FX.Additions.KubernetesClient.Models;

namespace ES.Kubernetes.Reflector.Mirroring.Core;

public class MirroringProperties
{
    public bool Allowed { get; set; }
    public string AllowedNamespaces { get; set; } = string.Empty;
    public bool AutoEnabled { get; set; }
    public string AutoNamespaces { get; set; } = string.Empty;
    public NamespacedName? Reflects { get; set; }

    public string ResourceVersion { get; set; } = string.Empty;


    public bool IsAutoReflection { get; set; }
    public string ReflectedVersion { get; set; } = string.Empty;
    public DateTimeOffset? ReflectedAt { get; set; }


    [MemberNotNullWhen(true, nameof(Reflects))]
    public bool IsReflection => Reflects != null;
}