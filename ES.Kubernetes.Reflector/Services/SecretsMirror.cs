using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Business;
using ES.Kubernetes.Reflector.Constants;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ES.Kubernetes.Reflector.Services
{
    public class SecretsMirror : ResourceReflector<V1Secret>, IHostedService
    {
        public SecretsMirror(ILogger<SecretsMirror> logger, IKubernetes apiClient, SecretsMonitor monitor) : base(
            logger, apiClient, monitor)
        {
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation("Starting");
            Subscribe();
            await Monitor.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation("Stopping");
            await Monitor.Stop();
            Unsubscribe();
        }

        protected override async Task<V1Secret> GetRequest(IKubernetes apiClient, string name, string ns)
        {
            return await apiClient.ReadNamespacedSecretAsync(name, ns);
        }

        protected override V1ObjectMeta GetMetadata(V1Secret resource)
        {
            return resource.Metadata;
        }

        protected override async Task Patch(V1Secret target, V1Secret source)
        {
            var patch = new JsonPatchDocument<V1Secret>();
            patch.Replace(e => e.Metadata.Annotations, new Dictionary<string, string>(target.Metadata.Annotations)
            {
                [Annotations.Reflection.ReflectedVersion] = source.Metadata.ResourceVersion,
                [Annotations.Reflection.ReflectedAt] = JsonConvert.SerializeObject(DateTimeOffset.UtcNow)
            });
            patch.Replace(e => e.Data, source.Data);

            await ApiClient.PatchNamespacedSecretWithHttpMessagesAsync(new V1Patch(patch),
                target.Metadata.Name, target.Metadata.NamespaceProperty);
        }
    }
}