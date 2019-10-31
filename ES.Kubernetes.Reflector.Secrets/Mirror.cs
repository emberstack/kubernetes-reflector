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

namespace ES.Kubernetes.Reflector.Secrets
{
    public class Mirror : ResourceMirror<V1Secret, V1SecretList>, IHostedService, IHealthCheck
    {
        public Mirror(ILogger<Mirror> logger, IKubernetes client,
            ManagedWatcher<V1Secret, V1SecretList> secretWatcher,
            ManagedWatcher<V1Namespace, V1NamespaceList> namespaceWatcher)
            : base(logger, client, secretWatcher, namespaceWatcher)
        {
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(IsFaulted ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy());
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await WatchersStart();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await WatchersStop();
        }


        protected override async Task<HttpOperationResponse<V1SecretList>> OnResourceWatcher(IKubernetes client)
        {
            return await client.ListSecretForAllNamespacesWithHttpMessagesAsync(watch: true);
        }

        protected override async Task<V1Secret> OnResourceAutoReflect(IKubernetes client, V1Secret item, string ns)
        {
            return await client.CreateNamespacedSecretAsync(new V1Secret
            {
                ApiVersion = item.ApiVersion,
                Kind = item.Kind,
                Type = item.Type,
                Data = item.Data,
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
            return client.DeleteNamespacedSecretAsync(name, ns);
        }

        protected override async Task<IList<V1Secret>> OnResourceList(IKubernetes client, string labelSelector = null,
            string fieldSelector = null)
        {
            return (await client.ListSecretForAllNamespacesAsync(fieldSelector: fieldSelector,
                labelSelector: labelSelector)).Items;
        }

        protected override Task<V1Secret> OnResourceGet(IKubernetes client, string name, string ns)
        {
            return client.ReadNamespacedSecretAsync(name, ns);
        }

        protected override async Task OnResourcePatch(IKubernetes client, V1Secret target, V1Secret source)
        {
            var patch = new JsonPatchDocument<V1Secret>();
            patch.Replace(e => e.Metadata.Annotations, new Dictionary<string, string>(target.Metadata.Annotations)
            {
                [Annotations.Reflection.ReflectedVersion] = source.Metadata.ResourceVersion,
                [Annotations.Reflection.ReflectedAt] = JsonConvert.SerializeObject(DateTimeOffset.UtcNow)
            });
            patch.Replace(e => e.Data, source.Data);

            await client.PatchNamespacedSecretWithHttpMessagesAsync(new V1Patch(patch),
                target.Metadata.Name, target.Metadata.NamespaceProperty);
        }
    }
}