using ES.Kubernetes.Reflector.Mirroring.Core;

namespace ES.Kubernetes.Reflector.Tests;

public sealed class ReflectorAnnotations
{
    private readonly Dictionary<string, string> _annotations = new();

    public ReflectorAnnotations WithReflectionAllowed(bool allowed)
    {
        _annotations[Annotations.Reflection.Allowed] = allowed.ToString().ToLower();
        return this;
    }

    public ReflectorAnnotations WithAllowedNamespaces(params string[] namespaces)
    {
        _annotations[Annotations.Reflection.AllowedNamespaces] = string.Join(",", namespaces);
        return this;
    }

    public ReflectorAnnotations WithAutoEnabled(bool enabled)
    {
        _annotations[Annotations.Reflection.AutoEnabled] = enabled.ToString().ToLower();
        return this;
    }

    public ReflectorAnnotations WithAutoNamespaces(bool enabled, params string[] namespaces)
    {
        _annotations[Annotations.Reflection.AutoNamespaces] = enabled ? string.Join(",", namespaces) : string.Empty;
        return this;
    }

    public Dictionary<string, string> Build()
    {
        if (_annotations.Count == 0)
        {
            _annotations[Annotations.Reflection.Allowed] = "true";
            _annotations[Annotations.Reflection.AllowedNamespaces] = string.Empty;
            _annotations[Annotations.Reflection.AutoEnabled] = "true";
            _annotations[Annotations.Reflection.AutoNamespaces] = string.Empty;
        }
        
        return _annotations;
    }
}