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

namespace ES.Kubernetes.Reflector.Services
{
    public class ConfigMapMirror : ResourceReflector<V1ConfigMap>, IHostedService
    {
        public ConfigMapMirror(ILogger<ConfigMapMirror> logger,
            IKubernetes apiClient,
            ConfigMapMonitor monitor) : base(logger, apiClient, monitor)
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

        protected override async Task<V1ConfigMap> GetRequest(IKubernetes apiClient, string name, string ns)
        {
            return await apiClient.ReadNamespacedConfigMapAsync(name, ns);
        }

        protected override V1ObjectMeta GetMetadata(V1ConfigMap resource)
        {
            return resource.Metadata;
        }

        protected override async Task Patch(V1ConfigMap target, V1ConfigMap source)
        {
            var patch = new JsonPatchDocument<V1ConfigMap>();
            patch.Replace(e => e.Metadata.Annotations, new Dictionary<string, string>(target.Metadata.Annotations)
            {
                [Annotations.Reflection.ReflectedVersion] = source.Metadata.ResourceVersion,
                [Annotations.Reflection.ReflectedAt] = DateTimeOffset.UtcNow.ToString()
            });
            patch.Replace(e => e.Data, source.Data);
            patch.Replace(e => e.BinaryData, source.BinaryData);

            await ApiClient.PatchNamespacedConfigMapAsync(new V1Patch(patch),
                target.Metadata.Name, target.Metadata.NamespaceProperty);
        }
    }
}