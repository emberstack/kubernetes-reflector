using System;
using System.Linq;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Resources
{
    public class KubernetesObjectId
    {
        public KubernetesObjectId(string @namespace, string name)
        {
            Name = name;
            Namespace = @namespace;
        }

        public KubernetesObjectId(string combined)
        {
            if (string.IsNullOrWhiteSpace(combined)) return;
            var split = combined.Split(new[] {"/"}, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!split.Any()) return;
            Namespace = split.First();
            Name = split.Last();
        }


        public KubernetesObjectId(V1ObjectMeta metadata)
        {
            Namespace = metadata.NamespaceProperty;
            Name = metadata.Name;
        }

        public string Namespace { get; }
        public string Name { get; }


        public bool Equals(KubernetesObjectId other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Namespace, other.Namespace) && string.Equals(Name, other.Name);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((KubernetesObjectId) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Namespace != null ? Namespace.GetHashCode() : 0) * 397) ^
                       (Name != null ? Name.GetHashCode() : 0);
            }
        }
    }
}