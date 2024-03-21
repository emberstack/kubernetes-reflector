using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Resources;

public sealed record KubeRef
{
    public static readonly KubeRef Empty = new(string.Empty, string.Empty);

    public KubeRef(string ns, string name)
    {
        if (string.IsNullOrWhiteSpace(ns))
        {
            ns = string.Empty;
        }

        Name = name ?? throw new ArgumentNullException(nameof(name), "Name cannot be null.");
        Namespace = ns;
    }

    public KubeRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        }

        var (namespacePart, namePart) = value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .ToTuple();

        Namespace = namespacePart ?? string.Empty;
        Name = namePart;
    }

    public KubeRef(V1ObjectMeta metadata) : this(metadata.Namespace() ?? string.Empty, metadata.Name)
    {
    }

    public string Namespace { get; }
    public string Name { get; }

    public static bool TryParse(string value, out KubeRef id)
    {
        id = Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var (namespacePart, namePart) = value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .ToTuple();

        if (string.IsNullOrEmpty(namespacePart) && !string.IsNullOrEmpty(namePart))
        {
            id = new KubeRef(string.Empty, namePart);
        }
        else if (!string.IsNullOrEmpty(namespacePart) && !string.IsNullOrEmpty(namePart))
        {
            id = new KubeRef(namespacePart, namePart);
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        this == obj ||
        (obj is KubeRef other &&
         string.Equals(Namespace, other.Namespace) &&
         string.Equals(Name, other.Name));

    public override int GetHashCode() =>
        (Namespace?.GetHashCode() ?? 0, Name?.GetHashCode() ?? 0);

    public override string ToString() =>
        (Namespace, Name) switch
        {
            ("", var name) => name,
            (var ns, var name) => $"{ns}/{name}"
        };
}
