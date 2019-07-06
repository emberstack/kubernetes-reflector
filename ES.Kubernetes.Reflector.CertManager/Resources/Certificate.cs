using k8s;
using k8s.Models;
using Newtonsoft.Json;

namespace ES.Kubernetes.Reflector.CertManager.Resources
{
    public class Certificate : IKubernetesObject
    {
        [JsonProperty(PropertyName = "metadata")]
        public V1ObjectMeta Metadata { get; set; }

        [JsonProperty(PropertyName = "spec")] public SpecDefinition Spec { get; set; }

        [JsonProperty(PropertyName = "apiVersion")]
        public string ApiVersion { get; set; }

        [JsonProperty(PropertyName = "kind")] public string Kind { get; set; }

        public class SpecDefinition
        {
            [JsonProperty(PropertyName = "secretName")]
            public string SecretName { get; set; }
        }
    }
}