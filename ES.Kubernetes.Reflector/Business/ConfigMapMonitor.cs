using System.Net;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Business
{
    public class ConfigMapMonitor : ResourceMonitor<V1ConfigMap>
    {
        public ConfigMapMonitor(ILogger<ConfigMapMonitor> logger, IKubernetes apiClient) : base(logger, apiClient)
        {
        }

        protected override async Task<HttpOperationResponse> ListRequest(IKubernetes apiClient)
        {
            try
            {
                return await apiClient.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true);
            }
            catch (HttpOperationException ex) when(ex.Response.StatusCode==HttpStatusCode.Forbidden)
            {
                Logger.LogError(ex,"Resource list request failed due to permissions.");
                throw;
            }
        }
    }
}