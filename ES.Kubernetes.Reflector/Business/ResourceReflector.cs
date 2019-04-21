using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Constants;
using ES.Kubernetes.Reflector.Models;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Business
{
    public abstract class ResourceReflector<T> where T : class, IKubernetesObject
    {
        private readonly ConcurrentDictionary<NamespacedResource, List<NamespacedResource>> _mirrors =
            new ConcurrentDictionary<NamespacedResource, List<NamespacedResource>>(
                NamespacedResourceEqualityComparer.Instance);

        private string _subscribeToken;

        protected ResourceReflector(ILogger logger, IKubernetes apiClient, ResourceMonitor<T> monitor)
        {
            Logger = logger;
            ApiClient = apiClient;
            Monitor = monitor;
        }

        protected ResourceMonitor<T> Monitor { get; }

        protected ILogger Logger { get; }
        protected IKubernetes ApiClient { get; }

        protected void Subscribe()
        {
            Logger.LogDebug("Subscribing to monitor events");
            Monitor.Unsubscribe(_subscribeToken);
            _subscribeToken = Monitor.Subscribe(ProcessEvent);
        }

        protected void Unsubscribe()
        {
            Logger.LogDebug("Unsubscribing from monitor events");
            Monitor.Unsubscribe(_subscribeToken);
        }

        private async Task ProcessEvent(WatchEventType eventType, T item)
        {
            var metadata = GetMetadata(item);
            if (metadata.Annotations == null) return;
            if (!metadata.Annotations.ContainsKey(Annotations.Reflection.Reflects) &&
                !metadata.Annotations.ContainsKey(Annotations.Reflection.Allowed)) return;
            Logger.LogDebug("{kind} {ns}/{name} was {eventType}", item.Kind, metadata.NamespaceProperty, metadata.Name,
                eventType);

            var key = new NamespacedResource(metadata);

            switch (eventType)
            {
                case WatchEventType.Added:
                case WatchEventType.Modified:

                    if (metadata.Annotations.TryGetValue(Annotations.Reflection.Reflects, out var reflects))
                    {
                        foreach (var mirror in _mirrors)
                            mirror.Value.RemoveAll(s => NamespacedResource.Comparer.Equals(s, key));

                        var source = new NamespacedResource(reflects);
                        if (!_mirrors.TryGetValue(source, out var mirrorList))
                        {
                            mirrorList = new List<NamespacedResource>();
                            _mirrors.AddOrUpdate(source, mirrorList, (_, __) => mirrorList);
                        }

                        if (!mirrorList.Contains(key, NamespacedResource.Comparer))
                            mirrorList.Add(key);

                        T sourceSecret = null;
                        try
                        {
                            sourceSecret = await GetRequest(ApiClient, source.Name, source.Namespace);
                        }
                        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                        {
                            Logger.LogWarning("{kind} {ns}/{name} cannot reflect {kind} {sourceNs}/{sourceName}." +
                                              " Source {kind} {sourceNs}/{sourceName} could not be found.",
                                item.Kind, key.Namespace, key.Name,
                                item.Kind, source.Namespace, source.Name,
                                item.Kind, source.Namespace, source.Name);
                        }

                        if (sourceSecret != null) await Mirror(sourceSecret, item);
                    }

                    if (metadata.ReflectionAllowed())
                    {
                        if (!_mirrors.TryGetValue(key, out var mirrorList))
                        {
                            mirrorList = new List<NamespacedResource>();
                            _mirrors.AddOrUpdate(key, mirrorList, (_, __) => mirrorList);
                        }

                        foreach (var mirror in mirrorList)
                        {
                            var targetSecret =
                                await GetRequest(ApiClient, mirror.Name, mirror.Namespace);
                            await Mirror(item, targetSecret);
                        }
                    }

                    break;
                case WatchEventType.Deleted:
                {
                    _mirrors.TryRemove(key, out _);
                    foreach (var mirror in _mirrors)
                    {
                        if (!mirror.Value.Any(s => NamespacedResource.Comparer.Equals(s, key))) continue;

                        Logger.LogDebug("Removing {kind} {ns}/{name} from {kind} {sourceNs}/{sourceName} mirror list",
                            item.Kind, key.Namespace, key.Name,
                            item.Kind, mirror.Key.Namespace, mirror.Key.Name);
                        mirror.Value.RemoveAll(s =>
                            NamespacedResourceEqualityComparer.Instance.Equals(s, key));
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


            if (sourceMeta.Annotations == null ||
                !sourceMeta.Annotations.TryGetValue(Annotations.Reflection.Allowed, out var allowedValue))
            {
                Logger.LogWarning(
                    "{kind} {sourceNs}/{sourceName} (version: {version}) cannot be reflected to {kind} {destNs}/{destName}." +
                    " Reflection allowed is not set.",
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                    target.Kind, targetMeta.NamespaceProperty, targetMeta.Name, targetMeta.NamespaceProperty);
                return;
            }

            if (!bool.TryParse(allowedValue, out var reflectionAllowed) && reflectionAllowed)
            {
                Logger.LogWarning(
                    "{kind} {sourceNs}/{sourceName} (version: {version}) cannot be reflected to {kind} {destNs}/{destName}." +
                    " Reflection allowed is not set to {true}.",
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                    target.Kind, targetMeta.NamespaceProperty, targetMeta.Name, targetMeta.NamespaceProperty, true);
                return;
            }


            if (sourceMeta.Annotations.TryGetValue(
                Annotations.Reflection.AllowedNamespaces, out var allowedNamespacesValue))
                if (!NamespaceMatch(allowedNamespacesValue, targetMeta.NamespaceProperty))
                {
                    Logger.LogWarning(
                        "{kind} {sourceNs}/{sourceName} (version: {version}) cannot be reflected to {kind} {destNs}/{destName}." +
                        " Namespace {destNs} is not in the allowed list.",
                        source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                        target.Kind, targetMeta.NamespaceProperty, targetMeta.Name, targetMeta.NamespaceProperty);
                    return;
                }

            var updateNeeded =
                !(targetMeta.Annotations.TryGetValue(Annotations.Reflection.ReflectedVersion, out var revisionValue) &&
                  sourceMeta.ResourceVersion == revisionValue);
            if (updateNeeded)
            {
                Logger.LogInformation(
                    "Reflecting {kind} {sourceNs}/{sourceName} (version: {version}) to {kind} {destNs}/{destName}",
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion,
                    target.Kind, targetMeta.NamespaceProperty, targetMeta.Name);

                await Patch(target, source);
            }
            else
            {
                Logger.LogDebug(
                    "{kind} {destNs}/{destName} matches source {kind} {sourceNs}/{sourceName} version ({revision}).",
                    target.Kind, targetMeta.NamespaceProperty, targetMeta.Name,
                    source.Kind, sourceMeta.NamespaceProperty, sourceMeta.Name, sourceMeta.ResourceVersion);
            }
        }

        protected abstract Task<T> GetRequest(IKubernetes apiClient, string name, string ns);
        protected abstract V1ObjectMeta GetMetadata(T resource);

        protected abstract Task Patch(T target, T source);

        private bool NamespaceMatch(string patterns, string value)
        {
            var regexPatterns = patterns.Split(",", StringSplitOptions.RemoveEmptyEntries);
            return regexPatterns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Select(pattern => Regex.Match(value, pattern))
                .Any(match => match.Success && match.Value.Length == value.Length);
        }
    }
}