using System.Reflection;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ES.Kubernetes.Reflector.Core.Json;

/// <summary>
///     Used to resolve <see cref="JsonPropertyNameAttribute" /> decorated contracts (breaking change since Kubernetes
///     client switched to System.Text.Json
/// </summary>
public class JsonPropertyNameContractResolver : DefaultContractResolver
{
    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        if (member.GetCustomAttribute<JsonPropertyNameAttribute>() is not { } propertyNameAttribute) return property;
        property.PropertyName = propertyNameAttribute.Name;
        return property;
    }
}