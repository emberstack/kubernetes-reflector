using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Mirroring;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json;

namespace ES.Kubernetes.Reflector.ConfigMaps
{
    public class Mirror : ResourceMirror<V1ConfigMap>, IHostedService, IHealthCheck
    {
        public Mirror(ILogger<Mirror> logger, IKubernetes client,
            ManagedWatcher<V1ConfigMap> configMapWatcher,
            ManagedWatcher<V1Namespace> namespaceWatcher)
            : base(logger, client, configMapWatcher, namespaceWatcher)
        {
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await WatchersStart();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await WatchersStop();
        }


        protected override async Task<HttpOperationResponse> OnResourceWatcher(IKubernetes client)
        {
            return await client.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true);
        }

        protected override async Task<V1ConfigMap> OnResourceAutoReflect(IKubernetes client, V1ConfigMap item,
            string ns)
        {
            return await client.CreateNamespacedConfigMapAsync(new V1ConfigMap
            {
                ApiVersion = item.ApiVersion,
                Kind = item.Kind,
                Data = item.Data,
                BinaryData = item.BinaryData,
                Metadata = new V1ObjectMeta
                {
                    Name = item.Metadata.Name,
                    NamespaceProperty = ns,
                    Annotations = new Dictionary<string, string>
                    {
                        [Annotations.Reflection.AutoReflects] = KubernetesObjectId.For(item.Metadata).ToString(),
                        [Annotations.Reflection.Reflects] = KubernetesObjectId.For(item.Metadata).ToString(),
                        [Annotations.Reflection.ReflectedVersion] = item.Metadata.ResourceVersion,
                        [Annotations.Reflection.ReflectedAt] = JsonConvert.SerializeObject(DateTimeOffset.UtcNow)
                    }
                }
            }, ns);
        }

        protected override Task OnResourceDelete(IKubernetes client, string name, string ns)
        {
            return client.DeleteNamespacedConfigMapAsync(name, ns);
        }

        protected override async Task<IList<V1ConfigMap>> OnResourceList(IKubernetes client,
            string labelSelector = null, string fieldSelector = null)
        {
            return (await client.ListConfigMapForAllNamespacesAsync(fieldSelector: fieldSelector,
                labelSelector: labelSelector)).Items;
        }

        protected override Task<V1ConfigMap> OnResourceGet(IKubernetes client, string name, string ns)
        {
            return client.ReadNamespacedConfigMapAsync(name, ns);
        }

        protected override async Task OnResourcePatch(IKubernetes client, V1ConfigMap target, V1ConfigMap source)
        {
            var patch = new JsonPatchDocument<V1ConfigMap>();
            patch.Replace(e => e.Metadata.Annotations, new Dictionary<string, string>(target.Metadata.Annotations)
            {
                [Annotations.Reflection.ReflectedVersion] = source.Metadata.ResourceVersion,
                [Annotations.Reflection.ReflectedAt] = JsonConvert.SerializeObject(DateTimeOffset.UtcNow)
            });
            patch.Replace(e => e.Data, source.Data);
            patch.Replace(e => e.BinaryData, source.BinaryData);

            await client.PatchNamespacedConfigMapWithHttpMessagesAsync(new V1Patch(patch),
                target.Metadata.Name, target.Metadata.NamespaceProperty);
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(IsFaulted ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy());
        }
    }
}