using System.ComponentModel;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Extensions;

public static class MetadataExtensions
{
    private static readonly Dictionary<Type, TypeConverter> Converters = new();

    public static IReadOnlyDictionary<string, string> SafeAnnotations(this V1ObjectMeta metadata)
    {
        return (IReadOnlyDictionary<string, string>)(metadata.Annotations ?? new Dictionary<string, string>());
    }

    public static KubeRef GetRef(this IKubernetesObject<V1ObjectMeta> resource)
    {
        return resource.EnsureMetadata().GetRef();
    }

    public static KubeRef GetRef(this V1ObjectMeta metadata)
    {
        return new KubeRef(metadata);
    }


    public static bool TryGet<T>(this IReadOnlyDictionary<string, string> annotations, string key, out T? value)
    {
        value = default;
        if (!annotations.TryGetValue(key, out var raw)) return false;
        try
        {
            if (Nullable.GetUnderlyingType(typeof(T)) != null)
            {
                if (!Converters.TryGetValue(typeof(T), out var conv))
                {
                    conv = TypeDescriptor.GetConverter(typeof(T));
                    Converters.TryAdd(typeof(T), conv);
                }

                value = (T?)conv.ConvertFromString(raw.Trim());
            }
            else
            {
                value = (T)Convert.ChangeType(raw.Trim(), typeof(T));
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}