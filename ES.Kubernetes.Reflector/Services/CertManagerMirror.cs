using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Business;
using ES.Kubernetes.Reflector.Constants;
using ES.Kubernetes.Reflector.Models;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json.Linq;

namespace ES.Kubernetes.Reflector.Services
{
    public class CertManagerMirror : IHostedService
    {
        private readonly IKubernetes _apiClient;
        private readonly IConfiguration _configuration;
        private readonly CertManagerCertificatesMonitor _certificatesMonitor;
        private readonly CustomResourceDefinitionMonitor _crdMonitor;
        private readonly ILogger<CertManagerMirror> _logger;
        private readonly SecretsMonitor _secretsMonitor;
        private string _crdMonitorSubscribeToken;
        private string _currentVersion;
        private readonly bool _enabled;

        public CertManagerMirror(ILogger<CertManagerMirror> logger, IKubernetes apiClient, IConfiguration configuration,
            CustomResourceDefinitionMonitor crdMonitor,
            CertManagerCertificatesMonitor certificatesMonitor,
            SecretsMonitor secretsMonitor)
        {
            _logger = logger;
            _apiClient = apiClient;
            _configuration = configuration;
            _crdMonitor = crdMonitor;
            _certificatesMonitor = certificatesMonitor;
            _secretsMonitor = secretsMonitor;
            _crdMonitor.Subscribe(OnCrdEvent);
            _certificatesMonitor.Subscribe(OnCertificatesEvent);
            _secretsMonitor.Subscribe(OnSecretsEvent);
            _enabled = bool.Parse(_configuration["Reflector:Extensions:CertManager:Enabled"]);
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Extension not enabled.");
                return;
            }

            _logger.LogInformation("Starting");
            _crdMonitorSubscribeToken = _crdMonitor.Subscribe(OnCrdEvent);
            await _crdMonitor.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_enabled) return;

            _logger.LogInformation("Stopping");
            await _crdMonitor.Stop();
            _crdMonitor.Unsubscribe(_crdMonitorSubscribeToken);
        }

        private async Task OnCrdEvent(WatchEventType eventType, V1beta1CustomResourceDefinition item)
        {
            if (eventType != WatchEventType.Added && eventType != WatchEventType.Modified) return;
            if (item.Spec?.Names == null) return;

            if (item.Spec.Group != CertManagerConstants.CrdGroup ||
                item.Spec.Names.Kind != CertManagerConstants.CertificateKind)
                return;

            if (_currentVersion == item.Spec.Version) return;

            _logger.LogInformation("Certificates definition set to version {version}", item.Spec.Version);
            _currentVersion = item.Spec.Version;
            if (_certificatesMonitor.IsMonitoring) await _certificatesMonitor.Stop();
            if (_secretsMonitor.IsMonitoring) await _secretsMonitor.Stop();
            _certificatesMonitor.CertificatesVersion = _currentVersion;
            await _certificatesMonitor.Start();
            await _secretsMonitor.Start();
        }

        private async Task OnSecretsEvent(WatchEventType eventType, V1Secret item)
        {
            if (eventType != WatchEventType.Added && eventType != WatchEventType.Modified) return;

            var metadata = item.Metadata;
            if (metadata.Labels == null) return;

            if (metadata.Labels.TryGetValue(CertManagerConstants.CertificateNameLabel, out var certificateName))
            {
                _logger.LogDebug("Secret {secretNs}/{secretName} belongs to certificate {certNs}/{certName}",
                    metadata.NamespaceProperty, metadata.Name,
                    metadata.NamespaceProperty, certificateName);

                CertificateDefinition cert = null;
                try
                {
                    var certificate = await _apiClient.GetNamespacedCustomObjectAsync(CertManagerConstants.CrdGroup,
                        _currentVersion, metadata.NamespaceProperty, CertManagerConstants.CertificatePlural,
                        certificateName);
                    cert = ((JObject)certificate).ToObject<CertificateDefinition>();
                }
                catch (HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Could not find certificate {certNs}/{certName}",
                        metadata.NamespaceProperty, certificateName);
                }

                if (cert != null) await Annotate(item, cert);
            }
        }

        private async Task OnCertificatesEvent(WatchEventType eventType, CertificateDefinition item)
        {
            if (eventType != WatchEventType.Added && eventType != WatchEventType.Modified) return;
            if (item.Metadata == null) return;
            if (item.Spec == null) return;

            _logger.LogDebug("Certificate {certNs}/{certName} has secret {secretNs}/{secretName}",
                item.Metadata.NamespaceProperty, item.Metadata.Name,
                item.Metadata.NamespaceProperty, item.Spec.SecretName);
            V1Secret secret = null;
            try
            {
                secret = await _apiClient.ReadNamespacedSecretAsync(item.Spec.SecretName,
                    item.Metadata.NamespaceProperty);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Could not find matching secret {secretNs}/{secretName}",
                    item.Metadata.NamespaceProperty, item.Spec.SecretName);
            }

            if (secret != null) await Annotate(secret, item);
        }


        private async Task Annotate(V1Secret secret, CertificateDefinition certificate)
        {
            var secretAnnotations =
                new Dictionary<string, string>(secret.Metadata.Annotations ?? new Dictionary<string, string>());
            var original = secretAnnotations.ToDictionary(s => s.Key, s => s.Value);

            var certificateAnnotations =
                new Dictionary<string, string>(certificate.Metadata.Annotations ?? new Dictionary<string, string>());

            if (certificateAnnotations.TryGetValue(Annotations.CertManagerCertificate.SecretReflectionAllowed,
                out var reflectionAllowed))
                secretAnnotations[Annotations.Reflection.Allowed] = reflectionAllowed;
            else
                secretAnnotations.Remove(Annotations.Reflection.Allowed);

            if (certificateAnnotations.TryGetValue(Annotations.CertManagerCertificate.SecretReflectionAllowedNamespaces,
                out var allowedNamespaces))
                secretAnnotations[Annotations.Reflection.AllowedNamespaces] = allowedNamespaces;
            else
                secretAnnotations.Remove(Annotations.Reflection.AllowedNamespaces);


            if (secretAnnotations.Count == original.Count &&
                secretAnnotations.Keys.All(s => original.ContainsKey(s)) &&
                secretAnnotations.All(s => original[s.Key] == s.Value))
            {
                _logger.LogDebug("Secret {secretNs}/{secretName} matches certificate {certNs}/{certName} reflection annotations",
                    secret.Metadata.NamespaceProperty, secret.Metadata.Name,
                    certificate.Metadata.NamespaceProperty, certificate.Metadata.Name);
                return;
            }

            _logger.LogInformation(
                "Patching {secretNs}/{secretName} to match certificate {certNs}/{certName} reflection annotations",
                secret.Metadata.NamespaceProperty, secret.Metadata.Name,
                certificate.Metadata.NamespaceProperty, certificate.Metadata.Name);
            var patch = new JsonPatchDocument<V1Secret>();
            patch.Replace(e => e.Metadata.Annotations, secretAnnotations);
            await _apiClient.PatchNamespacedSecretWithHttpMessagesAsync(new V1Patch(patch),
                secret.Metadata.Name, secret.Metadata.NamespaceProperty);
        }
    }
}