using System;
using System.Collections.Generic;
using System.Linq;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Models
{
    public class NamespacedResource
    {
        public NamespacedResource()
        {
        }

        public NamespacedResource(string combined)
        {
            if (string.IsNullOrWhiteSpace(combined)) return;
            var split = combined.Split("/", StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!split.Any()) return;
            Namespace = split.First();
            Name = split.Last();
        }

        public NamespacedResource(V1ObjectMeta metadata)
        {
            Namespace = metadata.NamespaceProperty;
            Name = metadata.Name;
        }


        public string Namespace { get; set; }
        public string Name { get; set; }

        public static IEqualityComparer<NamespacedResource> Comparer => NamespacedResourceEqualityComparer.Instance;
    }
}