﻿using System.Collections.Concurrent;
using System.Net;
using ES.Kubernetes.Reflector.Core.Extensions;
using ES.Kubernetes.Reflector.Core.Json;
using ES.Kubernetes.Reflector.Core.Messages;
using ES.Kubernetes.Reflector.Core.Mirroring.Constants;
using ES.Kubernetes.Reflector.Core.Mirroring.Extensions;
using ES.Kubernetes.Reflector.Core.Resources;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Newtonsoft.Json;

namespace ES.Kubernetes.Reflector.Core.Mirroring;

public abstract class ResourceMirror<TResource>(ILogger logger, IServiceProvider serviceProvider) :
    INotificationHandler<WatcherEvent>,
    INotificationHandler<WatcherClosed>
    where TResource : class, IKubernetesObject<V1ObjectMeta>
{
    private readonly ConcurrentDictionary<KubeRef, List<KubeRef>> _autoReflectionCache = new();
    private readonly ConcurrentDictionary<KubeRef, bool> _autoSources = new();
    private readonly ConcurrentDictionary<KubeRef, List<KubeRef>> _directReflectionCache = new();

    private readonly ConcurrentDictionary<KubeRef, bool> _notFoundCache = new();
    private readonly ConcurrentDictionary<KubeRef, ReflectorProperties> _propertiesCache = new();
    protected readonly ILogger Logger = logger;


    /// <summary>
    ///     Handles <see cref="WatcherClosed" /> notifications
    /// </summary>
    public Task Handle(WatcherClosed notification, CancellationToken cancellationToken)
    {
        //If not TResource or Namespace, not something this instance should handle
        if (notification.ResourceType != typeof(TResource) &&
            notification.ResourceType != typeof(V1Namespace)) return Task.CompletedTask;
        if (notification.ResourceType != typeof(TResource)) return Task.CompletedTask;

        Logger.LogDebug("Cleared sources for {Type} resources", typeof(TResource).Name);

        _autoSources.Clear();
        _notFoundCache.Clear();
        _propertiesCache.Clear();
        _autoReflectionCache.Clear();

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Handles <see cref="WatcherEvent" /> notifications
    /// </summary>
    public async Task Handle(WatcherEvent notification, CancellationToken cancellationToken)
    {
        switch (notification.Item)
        {
            case TResource resource:
                if (await OnResourceIgnoreCheck(resource)) return;

                Logger.LogTrace("Handling {eventType} {resourceType} {resourceRef}", notification.Type, resource.Kind,
                    resource.GetRef());

                var itemRef = resource.GetRef();

                //Remove from the not found, since it exists
                _notFoundCache.Remove(itemRef, out _);

                switch (notification.Type)
                {
                    case WatchEventType.Added:
                    case WatchEventType.Modified:
                    {
                        await HandleUpsert(resource, cancellationToken);
                    }
                        break;
                    case WatchEventType.Deleted:
                    {
                        _propertiesCache.Remove(itemRef, out _);
                        var properties = resource.GetReflectionProperties();


                        if (!properties.IsReflection)
                        {
                            if (properties is { Allowed: true, AutoEnabled: true } &&
                                _autoReflectionCache.TryGetValue(itemRef, out var reflectionList))
                                foreach (var reflectionId in reflectionList.ToArray())
                                {
                                    Logger.LogDebug("Deleting {id} - Source {sourceId} has been deleted", reflectionId,
                                        itemRef);
                                    await OnResourceDelete(reflectionId);
                                }

                            _autoSources.Remove(itemRef, out _);
                            _directReflectionCache.Remove(itemRef, out _);
                            _autoReflectionCache.Remove(itemRef, out _);
                        }
                        else
                        {
                            foreach (var item in _directReflectionCache) item.Value.Remove(itemRef);
                            foreach (var item in _autoReflectionCache) item.Value.Remove(itemRef);
                        }
                    }
                        break;
                    case WatchEventType.Error:
                    case WatchEventType.Bookmark:
                    default:
                        return;
                }

                break;
            case V1Namespace ns:
            {
                if (notification.Type != WatchEventType.Added) return;
                Logger.LogTrace("Handling {eventType} {resourceType} {resourceRef}", notification.Type, ns.Kind,
                    ns.GetRef());


                foreach (var autoSourceRef in _autoSources.Keys)
                {
                    var properties = _propertiesCache[autoSourceRef];
                    if (!properties.CanBeAutoReflectedToNamespace(ns.Name())) continue;


                    var reflectionRef = new KubeRef(ns.Name(), autoSourceRef.Name);
                    var autoReflectionList = _autoReflectionCache.GetOrAdd(autoSourceRef, new List<KubeRef>());

                    if (autoReflectionList.Contains(reflectionRef)) return;

                    await ResourceReflect(autoSourceRef, reflectionRef, null, null, true);

                    if (!autoReflectionList.Contains(reflectionRef))
                        autoReflectionList.Add(reflectionRef);
                }
            }
                break;
        }
    }


    private async Task HandleUpsert(TResource resource, CancellationToken cancellationToken)
    {
        var resourceRef = resource.GetRef();
        var properties = resource.GetReflectionProperties();

        //TODO: Investigate if we need to keep all or just properties of interest
        _propertiesCache.AddOrUpdate(resourceRef, properties, (_, _) => properties);

        //If this is not a reflection
        if (properties is not { IsReflection: true })
        {
            //Check any direct reflections if the binding is still valid
            if (_directReflectionCache.TryGetValue(resourceRef, out var reflectionList))
                foreach (var reflectionId in reflectionList.ToArray())
                    //Remove the reflection if the namespace is no longer allowed
                    if (!properties.CanBeReflectedToNamespace(reflectionId.Namespace))
                    {
                        Logger.LogInformation(
                            "Source {sourceId} no longer permits the direct reflection to {destinationId}.",
                            resourceRef, reflectionId);
                        reflectionList.Remove(reflectionId);
                    }

            //Delete any cached auto-reflections that are no longer valid
            if (_autoReflectionCache.TryGetValue(resourceRef, out reflectionList))
                foreach (var reflectionId in reflectionList.ToArray())
                {
                    if (properties.CanBeAutoReflectedToNamespace(reflectionId.Namespace)) continue;

                    reflectionList.Remove(reflectionId);

                    Logger.LogInformation(
                        "Source {sourceId} no longer permits the auto reflection to {destinationId}. Deleting {destinationId}.",
                        resourceRef, reflectionId, reflectionId);
                    await OnResourceDelete(reflectionId);
                }

            var isAutoSource = properties is { Allowed: true, AutoEnabled: true };

            //Update the status of an auto-source
            _autoSources.AddOrUpdate(resourceRef, isAutoSource, (_, _) => isAutoSource);

            //If not allowed or auto is disabled, remove the cache for auto-reflections
            if (!isAutoSource) _autoReflectionCache.Remove(resourceRef, out _);

            //If reflection is disabled, remove the reflections cache and stop reflecting
            if (!properties.Allowed)
            {
                _directReflectionCache.Remove(resourceRef, out _);
                return;
            }


            //Update known permitted direct reflections
            if (_directReflectionCache.TryGetValue(resourceRef, out reflectionList))
                foreach (var reflectionRef in reflectionList.ToArray())
                {
                    //Try to get the properties for the reflection. Otherwise, remove it
                    if (!_propertiesCache.TryGetValue(reflectionRef, out var reflectionProperties))
                    {
                        reflectionList.Remove(reflectionRef);
                        continue;
                    }

                    if (reflectionProperties.ReflectedVersion == properties.Version)
                    {
                        Logger.LogDebug("Skipping {id} - Source {sourceId} matches reflected version",
                            reflectionRef, resourceRef);
                        continue;
                    }

                    //Execute the reflection
                    await ResourceReflect(resourceRef, reflectionRef, resource, null, false);
                }

            //Ensure updated auto-reflections
            if (isAutoSource) await AutoReflectionForSource(resourceRef, resource, cancellationToken);


            return;
        }

        //If this is a direct reflection
        if (properties is { IsReflection: true, IsAutoReflection: false })
        {
            var sourceRef = properties.Reflects;
            ReflectorProperties sourceProperties;
            if (!_propertiesCache.TryGetValue(sourceRef, out var sourceProps))
            {
                var source = await TryResourceGet(sourceRef);
                if (source is null)
                {
                    Logger.LogWarning("Could not update {id} - Source {sourceId} could not be found.",
                        resourceRef, sourceRef);
                    return;
                }

                sourceProperties = source.GetReflectionProperties();
            }
            else
            {
                sourceProperties = sourceProps;
            }

            _propertiesCache.AddOrUpdate(sourceRef, sourceProperties, (_, _) => sourceProperties);
            _directReflectionCache.TryAdd(sourceRef, new List<KubeRef>());
            _directReflectionCache[sourceRef].Add(resourceRef);

            if (!sourceProperties.CanBeReflectedToNamespace(resourceRef.Namespace))
            {
                Logger.LogWarning("Could not update {id} - Source {sourceId} does not permit it.",
                    resourceRef, sourceRef);
                _directReflectionCache[sourceRef].Remove(resourceRef);
                return;
            }

            if (sourceProperties.Version == properties.ReflectedVersion)
            {
                Logger.LogDebug("Skipping {id} - Source {sourceId} matches reflected version",
                    resourceRef, sourceRef);
                return;
            }

            await ResourceReflect(sourceRef, resourceRef, null, resource, false);

            return;
        }

        //If this is an auto-reflection, ensure it still has a source. reflection will be done when we hit the source
        if (properties is { IsReflection: true, IsAutoReflection: true })
        {
            var sourceRef = properties.Reflects;

            //If the source is known to not exist, drop the reflection
            if (_notFoundCache.ContainsKey(sourceRef))
            {
                Logger.LogInformation("Source {sourceId} no longer exists. Deleting {destinationId}.",
                    sourceRef, resourceRef);
                await OnResourceDelete(resourceRef);
                return;
            }


            //Find the source resource
            ReflectorProperties sourceProperties;
            if (!_propertiesCache.TryGetValue(sourceRef, out var props))
            {
                var sourceResource = await TryResourceGet(sourceRef);
                if (sourceResource is null)
                {
                    Logger.LogInformation("Source {sourceId} no longer exists. Deleting {destinationId}.",
                        sourceRef, resourceRef);
                    await OnResourceDelete(resourceRef);
                    return;
                }

                sourceProperties = sourceResource.GetReflectionProperties();
            }
            else
            {
                sourceProperties = props;
            }

            _propertiesCache.AddOrUpdate(sourceRef, sourceProperties, (_, _) => sourceProperties);
            if (!sourceProperties.CanBeAutoReflectedToNamespace(resourceRef.Namespace))
            {
                Logger.LogInformation(
                    "Source {sourceId} no longer permits the auto reflection to {destinationId}. Deleting {destinationId}.",
                    sourceRef, resourceRef, resourceRef);
                await OnResourceDelete(resourceRef);
            }
        }
    }


    private async Task AutoReflectionForSource(KubeRef resourceRef, TResource? resourceInstance,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Processing auto-reflection source {id}", resourceRef);
        var properties = _propertiesCache[resourceRef];

        var autoReflectionList = _autoReflectionCache.GetOrAdd(resourceRef, _ => new List<KubeRef>());

        var matches = await OnResourceWithNameList(resourceRef.Name);
        using var client = serviceProvider.GetRequiredService<IKubernetes>();
        var namespaces = (await client.CoreV1.ListNamespaceAsync(cancellationToken: cancellationToken)).Items;

        foreach (var match in matches)
        {
            var matchProperties = match.GetReflectionProperties();
            _propertiesCache.AddOrUpdate(match.GetRef(), _ => matchProperties, (_, _) => matchProperties);
        }

        var toDelete = matches
            .Where(s => s.Namespace() != resourceRef.Namespace)
            .Where(m => !properties.CanBeAutoReflectedToNamespace(m.Namespace()))
            .Where(m => m.GetReflectionProperties().Reflects.Equals(resourceRef))
            .Select(s => s.GetRef()).ToList();

        foreach (var kubeRef in toDelete) await OnResourceDelete(kubeRef);

        resourceInstance ??= await TryResourceGet(resourceRef);
        if (resourceInstance is null) return;
        var resource = resourceInstance;

        var toCreate = namespaces
            .Where(s => s.Name() != resourceRef.Namespace)
            .Where(s =>
                matches.All(m => m.Namespace() != s.Name()) && properties.CanBeAutoReflectedToNamespace(s.Name()))
            .Select(s => new KubeRef(s.Name(), resource.Name())).ToList();

        var toUpdate = matches
            .Where(s => s.Namespace() != resourceRef.Namespace)
            .Where(m => !toDelete.Contains(m.GetRef()) && !toCreate.Contains(m.GetRef()) &&
                        m.GetReflectionProperties().ReflectedVersion != properties.Version &&
                        m.GetReflectionProperties().Reflects.Equals(resourceRef))
            .Select(m => m.GetRef()).ToList();

        var toSkip = matches
            .Where(s => s.Namespace() != resourceRef.Namespace)
            .Where(m => !toDelete.Contains(m.GetRef()) && !toCreate.Contains(m.GetRef()) &&
                        m.GetReflectionProperties().ReflectedVersion == properties.Version &&
                        m.GetReflectionProperties().Reflects.Equals(resourceRef))
            .Select(m => m.GetRef()).ToList();


        autoReflectionList.Clear();
        autoReflectionList.AddRange(toCreate);
        autoReflectionList.AddRange(toSkip);
        autoReflectionList.AddRange(toUpdate);

        foreach (var reflectionRef in toCreate) await ResourceReflect(resourceRef, reflectionRef, resource, null, true);
        foreach (var reflectionRef in toUpdate)
        {
            var reflection = matches.Single(s => s.GetRef().Equals(reflectionRef));
            await ResourceReflect(resourceRef, reflectionRef, resource, reflection, true);
        }

        Logger.LogInformation(
            "Auto-reflected {id} where permitted. Created {create} - Updated {update} - Deleted {delete} - Validated {skip}.",
            resourceRef, toCreate.Count, toUpdate.Count, toDelete.Count, toSkip.Count);
    }


    private async Task ResourceReflect(KubeRef sourceId, KubeRef targetId, TResource? sourceResource,
        TResource? targetResource, bool autoReflection)
    {
        if (sourceId.Equals(targetId)) return;

        Logger.LogDebug("Reflecting {sourceId} to {id}", sourceId, targetId);

        TResource source;
        if (sourceResource is null)
        {
            var lookup = await TryResourceGet(sourceId);
            if (lookup is not null)
            {
                source = lookup;
            }
            else
            {
                Logger.LogWarning("Could not update {id} - Source {sourceId} could not be found.", targetId, sourceId);
                return;
            }
        }
        else
        {
            source = sourceResource;
        }


        var patchAnnotations = new Dictionary<string, string>
        {
            [Annotations.Reflection.MetaAutoReflects] = autoReflection.ToString(),
            [Annotations.Reflection.Reflects] = sourceId.ToString(),
            [Annotations.Reflection.MetaReflectedVersion] = source.Metadata.ResourceVersion,
            [Annotations.Reflection.MetaReflectedAt] = JsonConvert.SerializeObject(DateTimeOffset.UtcNow)
        };


        try
        {
            if (targetResource is null)
            {
                var newResource = await OnResourceClone(source);
                newResource.Metadata ??= new V1ObjectMeta();
                newResource.Metadata.Name = targetId.Name;
                newResource.Metadata.NamespaceProperty = targetId.Namespace;
                newResource.Metadata.Annotations ??= new Dictionary<string, string>();
                var newResourceAnnotations = newResource.Metadata.Annotations;
                foreach (var patchAnnotation in patchAnnotations)
                    newResourceAnnotations[patchAnnotation.Key] = patchAnnotation.Value;
                newResourceAnnotations[Annotations.Reflection.MetaAutoReflects] = autoReflection.ToString();
                newResourceAnnotations[Annotations.Reflection.Reflects] = sourceId.ToString();
                newResourceAnnotations[Annotations.Reflection.MetaReflectedVersion] = source.Metadata.ResourceVersion;
                newResourceAnnotations[Annotations.Reflection.MetaReflectedAt] =
                    JsonConvert.SerializeObject(DateTimeOffset.UtcNow);

                try
                {
                    await OnResourceCreate(newResource, targetId.Namespace);
                    Logger.LogInformation("Created {id} as a reflection of {sourceId}", targetId, sourceId);
                    return;
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
                {
                    //If resource already exists, set target and fallback to patch
                    targetResource = await OnResourceGet(targetId);
                }
            }

            if (targetResource.GetReflectionProperties().ReflectedVersion == source.Metadata.ResourceVersion)
            {
                Logger.LogDebug("Skipping {id} - Source {sourceId} matches reflected version",
                    targetId, sourceId);
                return;
            }


            var patchDoc = new JsonPatchDocument<TResource>(new List<Operation<TResource>>(),
                new JsonPropertyNameContractResolver());
            var annotations = new Dictionary<string, string>(targetResource.Metadata.Annotations);
            foreach (var patchAnnotation in patchAnnotations)
                annotations[patchAnnotation.Key] = patchAnnotation.Value;
            patchDoc.Replace(e => e.Metadata.Annotations, annotations);

            await OnResourceConfigurePatch(source, patchDoc);

            var patch = JsonConvert.SerializeObject(patchDoc, Formatting.Indented);
            await OnResourceApplyPatch(new V1Patch(patch, V1Patch.PatchType.JsonPatch), targetId);
            Logger.LogInformation("Patched {id} as a reflection of {sourceId}", targetId, sourceId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not reflect {sourceId} to {targetId} due to exception.", sourceId, targetId);
        }
    }


    protected abstract Task OnResourceApplyPatch(V1Patch source, KubeRef refId);
    protected abstract Task OnResourceConfigurePatch(TResource source, JsonPatchDocument<TResource> patchDoc);
    protected abstract Task OnResourceCreate(TResource item, string ns);
    protected abstract Task<TResource> OnResourceClone(TResource sourceResource);
    protected abstract Task OnResourceDelete(KubeRef resourceId);


    protected abstract Task<TResource[]> OnResourceWithNameList(string itemRefName);

    private async Task<TResource?> TryResourceGet(KubeRef refId)
    {
        try
        {
            Logger.LogDebug("Retrieving {id}", refId);
            var resource = await OnResourceGet(refId);
            _notFoundCache.TryRemove(refId, out _);
            return resource;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            Logger.LogDebug("Could not find {id}", refId);
            _notFoundCache.TryAdd(refId, true);
            return null;
        }
    }

    protected abstract Task<TResource> OnResourceGet(KubeRef refId);

    protected virtual Task<bool> OnResourceIgnoreCheck(TResource item)
    {
        return Task.FromResult(false);
    }
}