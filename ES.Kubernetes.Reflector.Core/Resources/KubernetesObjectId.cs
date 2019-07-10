using System;
using System.Linq;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Core.Resources
{
    public class KubernetesObjectId
    {
        public static readonly KubernetesObjectId Empty = new KubernetesObjectId(string.Empty, string.Empty);

        public KubernetesObjectId(string @namespace, string name)
        {
            Name = name;
            Namespace = string.IsNullOrWhiteSpace(@namespace) ? null : @namespace;
        }

        public KubernetesObjectId(string combined)
        {
            if (string.IsNullOrWhiteSpace(combined)) return;
            var split = combined.Split(new[] {"/"}, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!split.Any()) throw new ArgumentException("Invalid value", nameof(combined));
            if (split.Count > 2) throw new ArgumentException("Invalid value", nameof(combined));
            if (split.Count == 1)
            {
                Name = split.Single();
            }
            else
            {
                Namespace = split.First();
                Name = split.Last();
            }
        }

        public KubernetesObjectId(V1ObjectMeta metadata) : this(metadata.NamespaceProperty, metadata.Name)
        {
        }

        public string Namespace { get; }
        public string Name { get; }

        public static bool TryParse(string value, out KubernetesObjectId id)
        {
            id = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var split = value.Split(new[] {"/"}, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!split.Any()) return false;
            if (split.Count > 2) return false;
            id = split.Count == 1
                ? new KubernetesObjectId(null, split.Single())
                : new KubernetesObjectId(split.First(), split.Last());

            return true;
        }

        public static KubernetesObjectId For(V1ObjectMeta metadata)
        {
            return new KubernetesObjectId(metadata);
        }


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

        public override string ToString()
        {
            return $"{(Namespace == null ? string.Empty : $"{Namespace}/")}{Name}";
        }
    }
}