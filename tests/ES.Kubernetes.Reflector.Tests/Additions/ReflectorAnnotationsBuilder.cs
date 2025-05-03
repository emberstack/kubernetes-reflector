using ES.Kubernetes.Reflector.Mirroring.Core;

namespace ES.Kubernetes.Reflector.Tests.Additions;

public sealed class ReflectorAnnotationsBuilder
{
    private readonly Dictionary<string, string> _annotations = new();

    public ReflectorAnnotationsBuilder WithReflectionAllowed(bool allowed)
    {
        _annotations[Annotations.Reflection.Allowed] = allowed.ToString().ToLower();
        return this;
    }

    public ReflectorAnnotationsBuilder WithAllowedNamespaces(params string[] namespaces)
    {
        _annotations[Annotations.Reflection.AllowedNamespaces] = string.Join(",", namespaces);
        return this;
    }

    public ReflectorAnnotationsBuilder WithAutoEnabled(bool enabled)
    {
        _annotations[Annotations.Reflection.AutoEnabled] = enabled.ToString().ToLower();
        return this;
    }

    public ReflectorAnnotationsBuilder WithAutoNamespaces(bool enabled, params string[] namespaces)
    {
        _annotations[Annotations.Reflection.AutoNamespaces] = enabled ? string.Join(",", namespaces) : string.Empty;
        return this;
    }

    public Dictionary<string, string> Build()
    {
        if (_annotations.Count != 0) return _annotations;

        _annotations[Annotations.Reflection.Allowed] = "true";
        _annotations[Annotations.Reflection.AllowedNamespaces] = string.Empty;
        _annotations[Annotations.Reflection.AutoEnabled] = "true";
        _annotations[Annotations.Reflection.AutoNamespaces] = string.Empty;
        return _annotations;
    }
}