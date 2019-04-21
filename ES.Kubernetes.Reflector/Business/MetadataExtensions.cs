using ES.Kubernetes.Reflector.Constants;
using ES.Kubernetes.Reflector.Models;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Business
{
    public static class MetadataExtensions
    {
        public static NamespacedResource ToNamespacedResource(this V1ObjectMeta metadata)
        {
            return new NamespacedResource(metadata);
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