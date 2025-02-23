using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Resources;

public sealed record KubeRef
{
    public static readonly KubeRef Empty = new(string.Empty, string.Empty);

    public KubeRef(string ns, string name)
    {
        Name = name;
        Namespace = string.IsNullOrWhiteSpace(ns) ? string.Empty : ns;
    }

    public KubeRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var split = value.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!split.Any()) return;
        switch (split.Count)
        {
            case > 2:
                throw new ArgumentException("Invalid value", nameof(value));
            case 1:
                Name = split.Single();
                break;
            default:
                Namespace = split.First();
                Name = split.Last();
                break;
        }
    }

    public KubeRef(V1ObjectMeta metadata) : this(metadata.Namespace() ?? string.Empty, metadata.Name)
    {
    }

    public string Namespace { get; } = string.Empty;
    public string Name { get; } = string.Empty;


    public bool Equals(KubeRef? other)
    {
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Namespace, other?.Namespace) && string.Equals(Name, other?.Name);
    }


    public static bool TryParse(string value, out KubeRef id)
    {
        id = Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var split = value.Trim().Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!split.Any()) return false;
        if (split.Count > 2) return false;
        id = split.Count == 1
            ? new KubeRef(string.Empty, split.Single().Trim())
            : new KubeRef(split.First().Trim(), split.Last().Trim());

        return true;
    }


    public override int GetHashCode()
    {
        unchecked
        {
            return ((!string.IsNullOrEmpty(Namespace) ? Namespace.GetHashCode() : 0) * 397) ^
                   (string.IsNullOrEmpty(Name) ? Name.GetHashCode() : 0);
        }
    }

    public override string ToString()
    {
        return $"{(string.IsNullOrEmpty(Namespace) ? string.Empty : $"{Namespace}/")}{Name}";
    }
}