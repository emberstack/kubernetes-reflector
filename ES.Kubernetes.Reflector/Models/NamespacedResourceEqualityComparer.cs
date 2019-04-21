using System.Collections.Generic;

namespace ES.Kubernetes.Reflector.Models
{
    public class NamespacedResourceEqualityComparer : IEqualityComparer<NamespacedResource>
    {
        public static NamespacedResourceEqualityComparer Instance { get; } = new NamespacedResourceEqualityComparer();

        public bool Equals(NamespacedResource x, NamespacedResource y)
        {
            return x.Namespace == y.Namespace && x.Name == y.Name;
        }

        public int GetHashCode(NamespacedResource obj)
        {
            return $"{obj.Namespace}/{obj.Name}".GetHashCode();
        }
    }
}