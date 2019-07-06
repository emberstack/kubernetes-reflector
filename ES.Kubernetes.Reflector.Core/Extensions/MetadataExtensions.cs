using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Extensions
{
    public static class MetadataExtensions
    {
        public static KubernetesObjectId ToKubernetesObjectId(this V1ObjectMeta metadata)
        {
            return new KubernetesObjectId(metadata);
        }

        public static bool ReflectionAllowed(this V1ObjectMeta metadata)
        {
            var canBeMirrored = false;
            if (metadata.Annotations.TryGetValue(Annotations.Reflection.Allowed, out var allowedValue))
                bool.TryParse(allowedValue, out canBeMirrored);
            return canBeMirrored;
        }
    }
}