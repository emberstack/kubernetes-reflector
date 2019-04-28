using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Constants;
using ES.Kubernetes.Reflector.Models;
using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Business
{
    public class CertManagerCertificatesMonitor : ResourceMonitor<CertificateDefinition>
    {
        public CertManagerCertificatesMonitor(ILogger<CertManagerCertificatesMonitor> logger, IKubernetes apiClient) :
            base(logger, apiClient)
        {
        }

        public string CertificatesVersion { get; set; } = "v1alpha1";

        protected override async Task<HttpOperationResponse> ListRequest(IKubernetes apiClient)
        {
            return await ApiClient.ListClusterCustomObjectWithHttpMessagesAsync(CertManagerConstants.CrdGroup,
                CertificatesVersion, CertManagerConstants.CertificatePlural, watch: true);
        }
    }
}