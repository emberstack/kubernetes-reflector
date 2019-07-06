using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.CertManager.Constants;
using ES.Kubernetes.Reflector.CertManager.Events;
using ES.Kubernetes.Reflector.CertManager.Resources;
using ES.Kubernetes.Reflector.Core.Constants;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json.Linq;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class CertificateSecretAnnotator :
        INotificationHandler<InternalCertificateWatcherEvent>,
        INotificationHandler<InternalSecretWatcherEvent>
    {
        private readonly IKubernetes _client;
        private readonly ILogger<CertificateSecretAnnotator> _logger;

        public CertificateSecretAnnotator(ILogger<CertificateSecretAnnotator> logger, IKubernetes client)
        {
            _logger = logger;
            _client = client;
        }


        public async Task Handle(InternalCertificateWatcherEvent notification, CancellationToken cancellationToken)
        {
            if (notification.Type != WatchEventType.Added && notification.Type != WatchEventType.Modified) return;
            if (notification.Item.Metadata == null) return;
            if (notification.Item.Spec == null) return;

            var certificate = notification.Item;

            _logger.LogDebug("Certificate {certNs}/{certName} has secret {secretNs}/{secretName}",
                certificate.Metadata.NamespaceProperty, certificate.Metadata.Name,
                certificate.Metadata.NamespaceProperty, certificate.Spec.SecretName);
            V1Secret secret = null;
            try
            {
                secret = await _client.ReadNamespacedSecretAsync(certificate.Spec.SecretName,
                    certificate.Metadata.NamespaceProperty, cancellationToken: cancellationToken);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Could not find matching secret {secretNs}/{secretName}",
                    certificate.Metadata.NamespaceProperty, certificate.Spec.SecretName);
            }

            if (secret != null) await Annotate(secret, certificate);
        }

        public async Task Handle(InternalSecretWatcherEvent notification, CancellationToken cancellationToken)
        {
            if (notification.Type != WatchEventType.Added && notification.Type != WatchEventType.Modified) return;

            var secret = notification.Item;
            var metadata = secret.Metadata;
            if (metadata.Labels == null) return;

            if (metadata.Labels.TryGetValue(CertManagerConstants.CertificateNameLabel, out var certificateName))
            {
                _logger.LogDebug("Secret {secretNs}/{secretName} belongs to certificate {certNs}/{certName}",
                    metadata.NamespaceProperty, metadata.Name,
                    metadata.NamespaceProperty, certificateName);

                Certificate certificate = null;
                try
                {
                    var certificateJObject = await _client.GetNamespacedCustomObjectAsync(CertManagerConstants.CrdGroup,
                        notification.CertificateResourceDefinitionVersion, metadata.NamespaceProperty,
                        CertManagerConstants.CertificatePlural,
                        certificateName, cancellationToken);
                    certificate = ((JObject) certificateJObject).ToObject<Certificate>();
                }
                catch (HttpOperationException exception) when (exception.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Could not find certificate {certNs}/{certName}",
                        metadata.NamespaceProperty, certificateName);
                }

                if (certificate != null) await Annotate(secret, certificate);
            }
        }


        private async Task Annotate(V1Secret secret, Certificate certificate)
        {
            var secretAnnotations =
                new Dictionary<string, string>(secret.Metadata.Annotations ?? new Dictionary<string, string>());
            var original = secretAnnotations.ToDictionary(s => s.Key, s => s.Value);

            var certAnnotations =
                new Dictionary<string, string>(certificate.Metadata.Annotations ?? new Dictionary<string, string>());

            void MatchAnnotations(Dictionary<string, string> pairs)
            {
                foreach (var pair in pairs)
                    if (certAnnotations.TryGetValue(pair.Key, out var value))
                        secretAnnotations[pair.Value] = value;
                    else
                        secretAnnotations.Remove(pair.Value);
            }

            MatchAnnotations(new Dictionary<string, string>
            {
                {
                    Annotations.CertManagerCertificate.SecretReflectionAllowed,
                    Annotations.Reflection.Allowed
                },
                {
                    Annotations.CertManagerCertificate.SecretReflectionAllowedNamespaces,
                    Annotations.Reflection.AllowedNamespaces
                }
            });


            if (secretAnnotations.Count == original.Count &&
                secretAnnotations.Keys.All(s => original.ContainsKey(s)) &&
                secretAnnotations.All(s => original[s.Key] == s.Value))
            {
                _logger.LogDebug(
                    "Secret {secretNs}/{secretName} matches certificate {certNs}/{certName} reflection annotations",
                    secret.Metadata.NamespaceProperty, secret.Metadata.Name,
                    certificate.Metadata.NamespaceProperty, certificate.Metadata.Name);
                return;
            }

            _logger.LogInformation(
                "Annotating secret {secretNamespace}/{secretName} to match certificate {certificateNamespace}/{certificateName} reflection annotations",
                secret.Metadata.NamespaceProperty, secret.Metadata.Name,
                certificate.Metadata.NamespaceProperty, certificate.Metadata.Name);

            var patch = new JsonPatchDocument<V1Secret>();
            patch.Replace(e => e.Metadata.Annotations, secretAnnotations);
            await _client.PatchNamespacedSecretWithHttpMessagesAsync(new V1Patch(patch),
                secret.Metadata.Name, secret.Metadata.NamespaceProperty);
        }
    }
}