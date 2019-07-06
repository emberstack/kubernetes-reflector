using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Reflection;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector.ConfigMaps
{
    public class Mirror : ResourceReflector<V1ConfigMap>, INotificationHandler<WatcherEvent<V1ConfigMap>>
    {
        private readonly ILogger<Mirror> _logger;

        private readonly ConcurrentDictionary<KubernetesObjectId, List<KubernetesObjectId>> _mirrors =
            new ConcurrentDictionary<KubernetesObjectId, List<KubernetesObjectId>>();

        public Mirror(ILogger<Mirror> logger, IKubernetes client) : base(logger, client)
        {
            _logger = logger;
        }

        public async Task Handle(WatcherEvent<V1ConfigMap> request, CancellationToken cancellationToken)
        {
            await OnWatcherEvent(request);
        }

        protected override Task<V1ConfigMap> GetRequest(IKubernetes apiClient, string name, string ns)
        {
            return apiClient.ReadNamespacedConfigMapAsync(name, ns);
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

            await Client.PatchNamespacedConfigMapAsync(new V1Patch(patch),
                target.Metadata.Name, target.Metadata.NamespaceProperty);
        }
    }
}