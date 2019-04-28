using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Business
{
    public class SecretsMonitor : ResourceMonitor<V1Secret>
    {
        public SecretsMonitor(ILogger<SecretsMonitor> logger, IKubernetes apiClient) : base(logger, apiClient)
        {
        }

        protected override async Task<HttpOperationResponse> ListRequest(IKubernetes apiClient)
        {
            return await apiClient.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true);
        }
    }
}