using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Constants;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Queuing;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core.Mirroring
{
    public abstract class ResourceMirror<TResource, TResourceList> where TResource : class, IKubernetesObject
    {
        /// <summary>
        ///     Keeps track of resources with auto-reflection turned on.
        /// </summary>
        private readonly ConcurrentDictionary<KubernetesObjectId, string> _autoReflections =
            new ConcurrentDictionary<KubernetesObjectId, string>();

        private readonly IKubernetes _client;
        private readonly FeederQueue<WatcherEvent> _eventQueue;
        private readonly ILogger _logger;
        private readonly ManagedWatcher<V1Namespace, V1NamespaceList> _namespaceWatcher;

        /// <summary>
        ///     Maps reflections to sources
        /// </summary>
        private readonly ConcurrentDictionary<KubernetesObjectId, KubernetesObjectId> _reflections =
            new ConcurrentDictionary<KubernetesObjectId, KubernetesObjectId>();

        private readonly ManagedWatcher<TResource, TResourceList> _resourceWatcher;


        protected ResourceMirror(ILogger logger, IKubernetes client,
            ManagedWatcher<TResource, TResourceList> resourceWatcher,
            ManagedWatcher<V1Namespace, V1NamespaceList> namespaceWatcher)
        {
            _logger = logger;
            _client = client;

            _resourceWatcher = resourceWatcher;
            _namespaceWatcher = namespaceWatcher;

            _eventQueue = new FeederQueue<WatcherEvent>(OnEvent, OnEventHandlingError);


            _resourceWatcher.EventHandlerFactory = e => _eventQueue.FeedAsync(e);
            _resourceWatcher.RequestFactory = OnResourceWatcher;
            _resourceWatcher.OnStateChanged = OnWatcherStateChanged;

            _namespaceWatcher.EventHandlerFactory = e => _eventQueue.FeedAsync(e);
            _namespaceWatcher.RequestFactory = async api =>
                await api.ListNamespaceWithHttpMessagesAsync(watch: true,
                    timeoutSeconds: Requests.WatcherTimeout);
            _namespaceWatcher.OnStateChanged = OnWatcherStateChanged;
        }

        protected bool IsFaulted => _namespaceWatcher.IsFaulted || _resourceWatcher.IsFaulted;


        protected async Task WatchersStart()
        {
            await _resourceWatcher.Start();
            await _namespaceWatcher.Start();
        }


        protected async Task WatchersStop()
        {
            await _resourceWatcher.Stop();
            await _namespaceWatcher.Stop();
        }


        private async Task OnEventHandlingError(WatcherEvent e, Exception ex)
        {
            var id = KubernetesObjectId.For(e.Item.Metadata());
            _logger.LogError(ex, "Failed to process {eventType} {kind} {@id} due to exception",
                e.Type, e.Item.Kind, id);
            await WatchersStop();
            _eventQueue.Clear();

            _logger.LogInformation("Restarting watchers ");
            await WatchersStart();
        }


        private async Task OnEvent(WatcherEvent e)
        {
            var id = KubernetesObjectId.For(e.Item.Metadata());
            _logger.LogTrace("[{eventType}] {kind} {@id}", e.Type, e.Item.Kind, id);
            switch (e)
            {
                case WatcherEvent<TResource> resourceEvent:
                    await OnResourceWatcherEvent(resourceEvent);
                    break;
                case WatcherEvent<V1Namespace> namespaceEvent:
                    await OnNamespaceWatcherEvent(namespaceEvent);
                    break;
            }
        }


        private async Task OnWatcherStateChanged<TGenericResource, TGenericResourceList>(
            ManagedWatcher<TGenericResource, TGenericResourceList, WatcherEvent<TGenericResource>> sender,
            ManagedWatcherStateUpdate update) where TGenericResource : class, IKubernetesObject
        {
            switch (update.State)
            {
                case ManagedWatcherState.Closed:
                case ManagedWatcherState.Faulted:
                    _logger.Log(update.State == ManagedWatcherState.Closed ? LogLevel.Debug : LogLevel.Warning,
                        update.Exception, "{type} watcher {state}", typeof(TGenericResource).Name, update.State);
                    await WatchersStop();
                    ReflectionsClear();
                    await WatchersStart();
                    break;
                default:
                    _logger.LogDebug("{type} watcher {state}", typeof(TGenericResource).Name, update.State);
                    break;
            }
        }


        protected async Task OnNamespaceWatcherEvent(WatcherEvent<V1Namespace> request)
        {
            switch (request.Type)
            {
                case WatchEventType.Added:
                {
                    var id = _autoReflections.Keys.ToList();
                    foreach (var source in id)
                    {
                        var item = await OnResourceGet(_client, source.Name, source.Namespace);
                        await CheckAutoReflections(item);
                    }
                }
                    break;
                case WatchEventType.Deleted:
                {
                    var toRemove = _reflections.Keys
                        .Where(s => s.Namespace.Equals(request.Item.Metadata.Name))
                        .ToList();
                    foreach (var id in toRemove) _reflections.TryRemove(id, out _);

                    toRemove = _autoReflections.Keys
                        .Where(s => s.Namespace.Equals(request.Item.Metadata.Name))
                        .ToList();
                    foreach (var id in toRemove) _autoReflections.TryRemove(id, out _);
                }
                    break;
            }
        }

        protected async Task OnResourceWatcherEvent(WatcherEvent<TResource> request)
        {
            var item = request.Item;
            var eventType = request.Type;
            var metadata = item.Metadata();
            var id = KubernetesObjectId.For(metadata);

            if (await OnResourceIgnoreCheck(item))
                return;

            switch (eventType)
            {
                case WatchEventType.Added:
                case WatchEventType.Modified:
                {
                    //Check if the source for auto reflection is still valid.
                    if (await CheckAutoReflectionSource(item)) return;

                    //Ensure auto reflections
                    await CheckAutoReflections(item);

                    //Update current item. If updated, return (Update will be picked up by watcher)
                    if (await ReflectSourceToSelf(item)) return;

                    //Update all child reflections
                    await ReflectSelfToReflections(item);
                }
                    break;
                case WatchEventType.Deleted:
                {
                    _reflections.TryRemove(id, out _);
                    _autoReflections.TryRemove(id, out _);

                    //Remove all auto-reflections
                    await UpdateAutoReflections(item, new List<string>());
                }
                    break;

                case WatchEventType.Error: break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        protected virtual Task<bool> OnResourceIgnoreCheck(TResource item)
        {
            return Task.FromResult(false);
        }

        private async Task<bool> CheckAutoReflectionSource(TResource item)
        {
            var metadata = item.Metadata();
            var id = KubernetesObjectId.For(metadata);
            var autoReflectionSource = metadata.AutoReflects();
            if (autoReflectionSource.Equals(KubernetesObjectId.Empty)) return false;
            var delete = false;
            try
            {
                var source = await OnResourceGet(_client,
                    autoReflectionSource.Name, autoReflectionSource.Namespace);
                var sourceMeta = source.Metadata();
                if (!sourceMeta.ReflectionAllowed() ||
                    !sourceMeta.ReflectionAutoEnabled() ||
                    !sourceMeta.ReflectionAllowedNamespacesMatch(id.Namespace) ||
                    !sourceMeta.ReflectionAutoNamespacesMatch(id.Namespace))
                    delete = true;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                delete = true;
            }

            if (!delete) return false;

            await OnResourceDelete(_client, id.Name, id.Namespace);
            _logger.LogInformation(
                "Deleted reflection {kind} for {@id} in namespace {targetNamespace}",
                item.Kind, autoReflectionSource, id.Namespace);
            return true;
        }

        private async Task CheckAutoReflections(TResource item)
        {
            var metadata = item.Metadata();
            var id = KubernetesObjectId.For(metadata);

            var namespaces = new List<string>();
            if (metadata.ReflectionAllowed() && metadata.ReflectionAutoEnabled())
            {
                _autoReflections[id] = metadata.ReflectionAutoNamespaces();
                var namespaceList = await _client.ListNamespaceAsync();
                namespaces = namespaceList.Items
                    .Select(s => s.Metadata.Name)
                    .Where(s => metadata.ReflectionAutoNamespacesMatch(s) &&
                                metadata.ReflectionAllowedNamespacesMatch(s))
                    .ToList();
            }
            else
            {
                _autoReflections.TryRemove(id, out _);
                namespaces.Clear();
            }

            namespaces.Remove(metadata.NamespaceProperty);

            await UpdateAutoReflections(item, namespaces);
        }

        private async Task UpdateAutoReflections(TResource item, List<string> namespaces)
        {
            var metadata = item.Metadata();
            var id = KubernetesObjectId.For(metadata);

            var autoReflections = await FindAutoReflections(id);

            var reflectionsToCreate = namespaces
                .Where(s => !autoReflections.Any(m => m.NamespaceProperty.Equals(s))).ToList();
            var reflectionsToRemove = autoReflections
                .Where(s => !namespaces.Contains(s.NamespaceProperty)).ToList();

            foreach (var reflectionNamespace in reflectionsToCreate)
            {
                var reflectionId = new KubernetesObjectId(reflectionNamespace, id.Name);
                try
                {
                    await OnResourceAutoReflect(_client, item, reflectionNamespace);

                    _logger.LogInformation(
                        "Created reflection {kind} for {@id} in namespace {namespace}.",
                        item.Kind, id, reflectionId.Namespace);
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
                {
                    _logger.LogWarning(
                        "Cannot create reflection {kind} {@reflectionId} for {@id}. " +
                        $"Found conflicting {{kind}} with the same name which was not created by {nameof(Reflector)}",
                        item.Kind, reflectionId, id, item.Kind);
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning(
                        "Cannot create reflection {kind} {@reflectionId} for {@id}. " +
                        "Access to namespace forbidden.",
                        item.Kind, reflectionId, id, item.Kind);
                }
            }


            foreach (var reflection in reflectionsToRemove)
            {
                await OnResourceDelete(_client, reflection.Name, reflection.NamespaceProperty);
                _reflections.TryRemove(KubernetesObjectId.For(reflection), out _);
                _logger.LogInformation(
                    "Deleted reflection {kind} for {@id} in namespace {targetNamespace}",
                    item.Kind, id, reflection.NamespaceProperty);
            }
        }

        private async Task<bool> ReflectSourceToSelf(TResource item)
        {
            var metadata = item.Metadata();
            var id = KubernetesObjectId.For(metadata);
            var sourceId = metadata.Reflects();
            if (sourceId.Equals(KubernetesObjectId.Empty))
            {
                _reflections.TryRemove(id, out _);
            }
            else
            {
                _reflections[id] = sourceId;

                TResource source = null;
                try
                {
                    source = await OnResourceGet(_client, sourceId.Name, sourceId.Namespace);
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("{kind} {@id} cannot be reflected." +
                                       " Source {@sourceId} could not be found",
                        item.Kind, id, sourceId);
                }

                if (source != null) await Reflect(source, item);


                return true;
            }

            return false;
        }

        private async Task ReflectSelfToReflections(TResource item)
        {
            var metadata = item.Metadata();
            var id = KubernetesObjectId.For(metadata);
            var reflections = _reflections.Where(s => s.Value.Equals(id)).Select(s => s.Key).ToList();
            foreach (var reflectionId in reflections)
                try
                {
                    var target = await OnResourceGet(_client, reflectionId.Name, reflectionId.Namespace);
                    await Reflect(item, target);
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(
                        "Could not reflect {@sourceId} to {@targetId}. Reflection {@targetId} not found.",
                        id, reflectionId, reflectionId);
                    _reflections.TryRemove(reflectionId, out _);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "Could not reflect {@sourceId} to {@targetId} due to exception",
                        id, reflectionId, reflectionId);
                }
        }

        private async Task<List<V1ObjectMeta>> FindAutoReflections(KubernetesObjectId id)
        {
            var autoMirrors = (await OnResourceList(_client, fieldSelector: $"metadata.name={id.Name}"))
                .Select(s => s.Metadata())
                .Where(s => s.NamespaceProperty != id.Namespace && s.AutoReflects().Equals(id))
                .ToList();
            return autoMirrors;
        }


        protected abstract Task<HttpOperationResponse<TResourceList>> OnResourceWatcher(IKubernetes client);

        protected abstract Task<TResource> OnResourceAutoReflect(IKubernetes client, TResource item, string ns);

        protected abstract Task OnResourceDelete(IKubernetes client, string name, string ns);

        protected abstract Task<IList<TResource>> OnResourceList(IKubernetes client, string labelSelector = null,
            string fieldSelector = null);

        protected abstract Task<TResource> OnResourceGet(IKubernetes client, string name, string ns);
        protected abstract Task OnResourcePatch(IKubernetes client, TResource target, TResource source);


        private async Task Reflect(TResource source, TResource target)
        {
            var sourceMeta = source.Metadata();
            var sourceId = KubernetesObjectId.For(sourceMeta);
            var targetMeta = target.Metadata();
            var targetId = KubernetesObjectId.For(targetMeta);


            //Do not mirror if source does not allow mirroring
            if (!sourceMeta.ReflectionAllowed())
            {
                _logger.LogWarning("{kind} {@sourceId} (version: {version}) cannot be reflected to {@targetId}. " +
                                   "Reflection not allowed by source {kind}",
                    source.Kind, sourceId, sourceMeta.ResourceVersion, targetId, source.Kind);
                return;
            }

            //Do not mirror if target namespace is not allowed
            if (!sourceMeta.ReflectionAllowedNamespacesMatch(targetMeta.NamespaceProperty))
            {
                _logger.LogWarning("{kind} {@sourceId} (version: {version}) cannot be reflected to {@targetId}. " +
                                   "Namespace {destNs} is not in the allowed list",
                    source.Kind, sourceId, sourceMeta.ResourceVersion, targetId, targetMeta.NamespaceProperty);
                return;
            }

            //Do not mirror if target matches source
            if (targetMeta.ReflectionVersionMatch(sourceMeta.ResourceVersion))
            {
                _logger.LogDebug(
                    "{kind} {@targetId} reflected version ({reflectedVersion}) matches source {@sourceId} version ({version})",
                    target.Kind, targetId, targetMeta.ReflectionVersion(), sourceId, sourceMeta.ResourceVersion);
                return;
            }


            _logger.LogTrace("Reflecting {kind} {@sourceId} (version: {version}) to {@targetId}",
                source.Kind, sourceId, sourceMeta.ResourceVersion, targetId);

            await OnResourcePatch(_client, target, source);

            _logger.LogInformation("Reflected {kind} {@sourceId} (version: {version}) to {@targetId}",
                source.Kind, sourceId, sourceMeta.ResourceVersion, targetId);
        }

        protected void ReflectionsClear()
        {
            _reflections.Clear();
            _autoReflections.Clear();
        }
    }
}