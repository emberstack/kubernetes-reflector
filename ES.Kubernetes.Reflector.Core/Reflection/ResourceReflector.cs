using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core.Reflection
{
    public abstract class ResourceReflector<T> where T : class, IKubernetesObject
    {
        private readonly ConcurrentDictionary<KubernetesObjectId, List<KubernetesObjectId>> _mirrors =
            new ConcurrentDictionary<KubernetesObjectId, List<KubernetesObjectId>>();

        protected ResourceReflector(ILogger logger, IKubernetes client)
        {
            Logger = logger;
            Client = client;
        }

        protected ILogger Logger { get; }
        protected IKubernetes Client { get; }


        protected async Task OnWatcherEvent(WatcherEvent<T> request)
        {
            var item = request.Item;
            var eventType = request.Type;
            var metadata = GetMetadata(request.Item);
            if (metadata.Annotations == null) return;
            if (!metadata.Annotations.ContainsKey(Annotations.Reflection.Reflects) &&
                !metadata.Annotations.ContainsKey(Annotations.Reflection.Allowed)) return;
            Logger.LogDebug("{kind} {namespace}/{name} was {eventType}",
                item.Kind, metadata.NamespaceProperty, metadata.Name, eventType);

            var key = new KubernetesObjectId(metadata);

            switch (eventType)
            {
                case WatchEventType.Added:
                case WatchEventType.Modified:

                    if (metadata.Annotations.TryGetValue(Annotations.Reflection.Reflects, out var reflects))
                    {
                        //Remove from all mirrors since the source value might have changed.
                        foreach (var mirror in _mirrors) mirror.Value.RemoveAll(s => s.Equals(key));

                        var sourceId = new KubernetesObjectId(reflects);

                        //Create mirror list for source if it doesn't exist
                        if (!_mirrors.TryGetValue(sourceId, out var mirrorList))
                        {
                            mirrorList = new List<KubernetesObjectId>();
                            _mirrors.AddOrUpdate(sourceId, mirrorList, (_, __) => mirrorList);
                        }

                        //Add to source mirror list
                        if (!mirrorList.Contains(key)) mirrorList.Add(key);

                        T source = null;
                        try
                        {
                            source = await GetRequest(Client, sourceId.Name, sourceId.Namespace);
                        }
                        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                        {
                            Logger.LogWarning("{kind} {ns}/{name} cannot be reflected." +
                                              " Source {sourceNs}/{sourceName} could not be found.",
                                item.Kind, key.Namespace, key.Name,
                                sourceId.Namespace, sourceId.Name,
                                sourceId.Namespace, sourceId.Name);
                        }

                        if (source != null) await Mirror(source, item);
                    }

                    if (metadata.ReflectionAllowed())
                    {
                        //Create mirror list if it doesn't exist
                        if (!_mirrors.TryGetValue(key, out var mirrorList))
                        {
                            mirrorList = new List<KubernetesObjectId>();
                            _mirrors.AddOrUpdate(key, mirrorList, (_, __) => mirrorList);
                        }

                        //Update all mirrors
                        foreach (var mirror in mirrorList)
                        {
                            var target = await GetRequest(Client, mirror.Name, mirror.Namespace);
                            await Mirror(item, target);
                        }
                    }

                    break;
                case WatchEventType.Deleted:
                {
                    //Remove any owned mirror list
                    _mirrors.TryRemove(key, out _);

                    //Remove item from other mirror lists
                    foreach (var mirror in _mirrors)
                    {
                        if (!mirror.Value.Any(s => s.Equals(key))) continue;

                        Logger.LogDebug("Removing {kind} {ns}/{name} from {sourceNs}/{sourceName} mirror list",
                            item.Kind, key.Namespace, key.Name,
                            mirror.Key.Namespace, mirror.Key.Name);
                        mirror.Value.RemoveAll(s => Equals(key));
                    }
                }
                    break;
                case WatchEventType.Error:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task Mirror(T source, T target)
        {
            var sourceMeta = GetMetadata(source);
            var targetMeta = GetMetadata(target);

            //Do not mirror if source does not allow mirroring
            if (sourceMeta.Annotations == null ||
                !sourceMeta.Annotations.TryGetValue(Annotations.Reflection.Allowed, out var allowedValue))
            {
                Logger.LogWarning(
                    "{kind} {sourceNs}/{sourceName} (version: {version}) cannot be reflected to {destNs}/{destName}." +
                    " Reflection allowed is not set.",
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                    targetMeta.NamespaceProperty, targetMeta.Name, targetMeta.NamespaceProperty);
                return;
            }

            //Do not mirror if source mirroring is not set to 'true'
            if (!bool.TryParse(allowedValue, out var reflectionAllowed) && reflectionAllowed)
            {
                Logger.LogWarning(
                    "{kind} {sourceNs}/{sourceName} (version: {version}) cannot be reflected to {destNs}/{destName}." +
                    " Reflection allowed is not set to {allowedValue}.",
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                    targetMeta.NamespaceProperty, targetMeta.Name, targetMeta.NamespaceProperty, true);
                return;
            }


            //Do not mirror if target namespace is not allowed
            if (sourceMeta.Annotations.TryGetValue(
                Annotations.Reflection.AllowedNamespaces, out var allowedNamespacesValue))
                if (!NamespaceMatch(targetMeta.NamespaceProperty, allowedNamespacesValue))
                {
                    Logger.LogWarning(
                        "{kind} {sourceNs}/{sourceName} (version: {version}) cannot be reflected to {destNs}/{destName}." +
                        " Namespace {destNs} is not in the allowed list.",
                        source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                        targetMeta.NamespaceProperty, targetMeta.Name, targetMeta.NamespaceProperty);
                    return;
                }

            var updateNeeded =
                !(targetMeta.Annotations.TryGetValue(Annotations.Reflection.ReflectedVersion, out var revisionValue) &&
                  sourceMeta.ResourceVersion == revisionValue);
            if (updateNeeded)
            {
                Logger.LogInformation(
                    "Reflecting {kind} {sourceNs}/{sourceName} (version: {version}) to {destNs}/{destName}",
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                    targetMeta.NamespaceProperty, targetMeta.Name);

                await Patch(target, source);
            }
            else
            {
                Logger.LogDebug(
                    "{kind} {destNs}/{destName} matches source {sourceNs}/{sourceName} version ({revision}).",
                    target.Kind, targetMeta.NamespaceProperty, targetMeta.Name,
                    sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion);
            }
        }

        protected abstract Task<T> GetRequest(IKubernetes apiClient, string name, string ns);
        protected abstract V1ObjectMeta GetMetadata(T resource);

        protected abstract Task Patch(T target, T source);

        private bool NamespaceMatch(string value, string patterns)
        {
            var regexPatterns = patterns.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries);
            return regexPatterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Select(pattern => Regex.Match(value, pattern))
                .Any(match => match.Success && match.Value.Length == value.Length);
        }
    }
}