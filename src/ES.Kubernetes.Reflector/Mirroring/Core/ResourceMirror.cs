using System.Collections.Concurrent;
using System.Net;
using ES.FX.Additions.KubernetesClient.Models;
using ES.FX.Additions.KubernetesClient.Models.Extensions;
using ES.FX.Additions.Newtonsoft.Json.Serialization;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using Newtonsoft.Json;

namespace ES.Kubernetes.Reflector.Mirroring.Core;

public abstract class ResourceMirror<TResource>(ILogger logger, IKubernetes kubernetes) :
    IWatcherEventHandler, IWatcherClosedHandler
    where TResource : class, IKubernetesObject<V1ObjectMeta>
{
    private static readonly IDictionary<string, string> EmptyLabels = new Dictionary<string, string>();

    private readonly ConcurrentDictionary<NamespacedName, HashSet<NamespacedName>> _autoReflectionCache = new();
    private readonly ConcurrentDictionary<NamespacedName, bool> _autoSources = new();
    private readonly ConcurrentDictionary<NamespacedName, HashSet<NamespacedName>> _directReflectionCache = new();

    private readonly ConcurrentDictionary<string, V1Namespace> _namespaceCache = new();
    private readonly ConcurrentDictionary<NamespacedName, bool> _notFoundCache = new();
    private readonly ConcurrentDictionary<NamespacedName, MirroringProperties> _propertiesCache = new();
    private readonly ConcurrentDictionary<NamespacedName, string> _lastWarnedSelectorErrors = new();
    protected readonly IKubernetes Kubernetes = kubernetes;
    protected readonly ILogger Logger = logger;


    /// <summary>
    ///     Handles <see cref="WatcherClosed" /> notifications
    /// </summary>
    public Task Handle(WatcherClosed notification, CancellationToken cancellationToken)
    {
        //If not TResource or Namespace, not something this instance should handle
        if (notification.ResourceType != typeof(TResource) &&
            notification.ResourceType != typeof(V1Namespace)) return Task.CompletedTask;

        if (notification.ResourceType == typeof(V1Namespace))
        {
            Logger.LogDebug("Cleared namespace cache for {Type} resources", typeof(TResource).Name);
            _namespaceCache.Clear();
            return Task.CompletedTask;
        }

        // Preserve _autoSources, _propertiesCache, and _namespaceCache across resource watcher
        // restarts. The NamespaceWatcher runs independently and can deliver namespace-added events
        // while this resource watcher is reconnecting. Without these caches the namespace handler
        // iterates an empty _autoSources and silently skips mirror creation — a race that causes
        // new namespaces to never receive their auto-reflected resources until the next full
        // watcher cycle. Stale entries are harmless: the replay overwrites them, and ResourceReflect
        // always re-fetches the source from the API when sourceObj is null.
        Logger.LogDebug("Clearing relationship caches for {Type} resources (preserving auto-sources and properties)",
            typeof(TResource).Name);

        _notFoundCache.Clear();
        _autoReflectionCache.Clear();
        _directReflectionCache.Clear();
        _lastWarnedSelectorErrors.Clear();

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Handles <see cref="WatcherEvent" /> notifications
    /// </summary>
    public async Task Handle(WatcherEvent notification, CancellationToken cancellationToken)
    {
        switch (notification.Item)
        {
            case TResource obj:
                if (await OnResourceIgnoreCheck(obj)) return;
                var objNsName = obj.ObjectReference().NamespacedName();

                Logger.LogTrace("Handling {eventType} {resourceType} {resourceNsName}",
                    notification.EventType, obj.Kind, obj.NamespacedName());


                //Remove from the not found, since it exists
                _notFoundCache.Remove(obj.NamespacedName(), out _);

                switch (notification.EventType)
                {
                    case WatchEventType.Added:
                    case WatchEventType.Modified:
                        await HandleUpsert(obj, cancellationToken);
                        break;
                    case WatchEventType.Deleted:
                    {
                        _propertiesCache.Remove(objNsName, out _);
                        _lastWarnedSelectorErrors.TryRemove(objNsName, out _);
                        var properties = obj.GetMirroringProperties();

                        if (!properties.IsReflection)
                        {
                            if (properties is { Allowed: true, AutoEnabled: true } &&
                                _autoReflectionCache.TryGetValue(objNsName, out var reflectionList))
                                foreach (var reflectionNsName in reflectionList.ToArray())
                                {
                                    Logger.LogDebug("Deleting {objNsName} - Source {sourceNsName} has been deleted",
                                        reflectionNsName, objNsName);
                                    await OnResourceDelete(reflectionNsName);
                                }

                            _autoSources.Remove(objNsName, out _);
                            _directReflectionCache.Remove(objNsName, out _);
                            _autoReflectionCache.Remove(objNsName, out _);
                        }
                        else
                        {
                            foreach (var item in _directReflectionCache) item.Value.Remove(objNsName);
                            foreach (var item in _autoReflectionCache) item.Value.Remove(objNsName);
                        }
                    }
                        break;
                    case WatchEventType.Error:
                    case WatchEventType.Bookmark:
                    default:
                        return;
                }

                break;
            case V1Namespace ns when notification.EventType is WatchEventType.Added or WatchEventType.Modified:
            {
                Logger.LogTrace("Handling {eventType} {resourceType} {resourceRef}", notification.EventType, ns.Kind,
                    ns.ObjectReference().NamespacedName());

                // Skip reconciliation when only non-label fields changed (status, annotations, resourceVersion).
                // Reflection eligibility is purely a function of namespace name and labels.
                if (notification.EventType == WatchEventType.Modified &&
                    _namespaceCache.TryGetValue(ns.Name(), out var cachedNs) &&
                    NamespaceLabelsEqual(cachedNs, ns))
                    break;

                //Cache the namespace for label selector lookups
                _namespaceCache.AddOrUpdate(ns.Name(), ns, (_, _) => ns);

                //Update all auto-sources
                foreach (var sourceNsName in _autoSources.Keys)
                {
                    if (!_propertiesCache.TryGetValue(sourceNsName, out var properties)) continue;

                    var autoReflections = _autoReflectionCache.GetOrAdd(sourceNsName, []);
                    var reflectionNsName = sourceNsName with { Namespace = ns.Name() };

                    if (properties.CanBeAutoReflectedToNamespace(ns))
                    {
                        //Create or update the auto-reflection in this namespace
                        await ResourceReflect(
                            sourceNsName,
                            reflectionNsName,
                            null,
                            null,
                            true,
                            cancellationToken);

                        autoReflections.Add(reflectionNsName);
                    }
                    else if (autoReflections.Remove(reflectionNsName))
                    {
                        //Namespace no longer matches — remove the auto-reflection
                        Logger.LogDebug(
                            "Deleting {reflectionNsName} - namespace {ns} no longer matches selector for source {sourceNsName}",
                            reflectionNsName, ns.Name(), sourceNsName);
                        await OnResourceDelete(reflectionNsName);
                    }
                }

                //Rebalance any direct reflections targeting this namespace against current labels
                foreach (var (sourceNsName, reflectionList) in _directReflectionCache)
                {
                    if (!_propertiesCache.TryGetValue(sourceNsName, out var sourceProperties)) continue;

                    var staleReflections = reflectionList
                        .Where(r => r.Namespace == ns.Name())
                        .ToList();
                    if (staleReflections.Count == 0) continue;

                    if (CanBeReflectedToNamespaceCached(sourceProperties, ns.Name())) continue;

                    foreach (var reflectionNsName in staleReflections)
                    {
                        Logger.LogInformation(
                            "Source {sourceNsName} no longer permits the direct reflection to {reflectionNsName}.",
                            sourceNsName, reflectionNsName);
                        reflectionList.Remove(reflectionNsName);
                    }
                }
            }
                break;
            case V1Namespace ns when notification.EventType == WatchEventType.Deleted:
            {
                Logger.LogTrace("Handling {eventType} {resourceType} {resourceRef}", notification.EventType, ns.Kind,
                    ns.ObjectReference().NamespacedName());

                _namespaceCache.TryRemove(ns.Name(), out _);

                //Remove any auto-reflections targeting this namespace
                foreach (var sourceNsName in _autoSources.Keys)
                {
                    var autoReflections = _autoReflectionCache.GetOrAdd(sourceNsName, []);
                    var reflectionNsName = sourceNsName with { Namespace = ns.Name() };
                    autoReflections.Remove(reflectionNsName);
                }

                //Remove any direct reflections targeting this namespace
                foreach (var reflectionList in _directReflectionCache.Values)
                    reflectionList.RemoveWhere(r => r.Namespace == ns.Name());
            }
                break;
        }
    }


    private async Task HandleUpsert(TResource obj, CancellationToken cancellationToken)
    {
        var objNsName = obj.NamespacedName();
        var objProperties = obj.GetMirroringProperties();

        _propertiesCache.AddOrUpdate(objNsName, objProperties,
            (_, _) => objProperties);

        WarnOnInvalidLabelSelectors(objNsName, objProperties);

        switch (objProperties)
        {
            //If the resource is not a reflection
            case { IsReflection: false }:
            {
                //Remove any direct reflections that are no longer valid
                if (_directReflectionCache.TryGetValue(objNsName, out var reflectionList))
                {
                    var reflections = reflectionList
                        .Where(s => !CanBeReflectedToNamespaceCached(objProperties, s.Namespace))
                        .ToHashSet();

                    foreach (var reflectionNsName in reflections)
                    {
                        Logger.LogInformation(
                            "Source {sourceNsName} no longer permits the direct reflection to {reflectionNsName}.",
                            objNsName, reflectionNsName);
                        reflectionList.Remove(reflectionNsName);
                    }
                }


                //Delete any cached auto-reflections that are no longer valid
                if (_autoReflectionCache.TryGetValue(objNsName, out reflectionList))
                {
                    var reflections = reflectionList
                        .Where(s => !CanBeReflectedToNamespaceCached(objProperties, s.Namespace))
                        .ToHashSet();
                    foreach (var reflectionNsName in reflections)
                    {
                        reflectionList.Remove(reflectionNsName);

                        Logger.LogInformation(
                            "Source {sourceNsName} no longer permits the auto reflection to {reflectionNsName}. " +
                            "Deleting {reflectionNsName}.",
                            objNsName, reflectionNsName, reflectionNsName);
                        await OnResourceDelete(reflectionNsName);
                    }
                }


                var isAutoSource = objProperties is { Allowed: true, AutoEnabled: true };

                //Update the status of an auto-source
                _autoSources.AddOrUpdate(objNsName, isAutoSource, (_, _) => isAutoSource);

                //If not allowed or auto is disabled, remove the cache for auto-reflections
                if (!isAutoSource) _autoReflectionCache.Remove(objNsName, out _);

                //If reflection is disabled, remove the reflections cache and stop reflecting
                if (!objProperties.Allowed)
                {
                    _directReflectionCache.Remove(objNsName, out _);
                    return;
                }

                //Update known permitted direct reflections
                if (_directReflectionCache.TryGetValue(objNsName, out reflectionList))
                    foreach (var reflectionNsName in reflectionList.ToArray())
                    {
                        //Try to get the properties for the reflection. Otherwise, remove it
                        if (!_propertiesCache.TryGetValue(reflectionNsName, out var reflectionProperties))
                        {
                            reflectionList.Remove(reflectionNsName);
                            continue;
                        }

                        if (reflectionProperties.ReflectedVersion == objProperties.ResourceVersion)
                        {
                            Logger.LogDebug(
                                "Skipping {reflectionNsName} - Source {sourceNsName} matches reflected version",
                                reflectionNsName, objNsName);
                            continue;
                        }

                        //Execute the reflection
                        await ResourceReflect(objNsName,
                            reflectionNsName,
                            obj,
                            null,
                            false,
                            cancellationToken);
                    }

                //Ensure updated auto-reflections
                if (isAutoSource) await AutoReflectionForSource(objNsName, obj, cancellationToken);


                return;
            }
            //If resource is a direct reflection
            case { IsReflection: true, IsAutoReflection: false }:
            {
                var sourceNsName = objProperties.Reflects;
                MirroringProperties sourceProperties;
                if (!_propertiesCache.TryGetValue(sourceNsName, out var sourceProps))
                {
                    var sourceObj = await TryResourceGet(sourceNsName, cancellationToken);
                    if (sourceObj is null)
                    {
                        Logger.LogWarning(
                            "Could not update {reflectionNsName} - Source {sourceNsName} could not be found.",
                            objNsName, sourceNsName);
                        return;
                    }

                    sourceProperties = sourceObj.GetMirroringProperties();
                }
                else
                {
                    sourceProperties = sourceProps;
                }

                _propertiesCache.AddOrUpdate(sourceNsName,
                    sourceProperties, (_, _) => sourceProperties);
                _directReflectionCache.TryAdd(sourceNsName, []);
                _directReflectionCache[sourceNsName].Add(objNsName);

                if (!CanBeReflectedToNamespaceCached(sourceProperties, objNsName.Namespace))
                {
                    Logger.LogWarning("Could not update {reflectionNsName} - Source {sourceNsName} does not permit it.",
                        objNsName, sourceNsName);

                    _directReflectionCache[sourceNsName]
                        .Remove(objNsName);
                    return;
                }

                if (sourceProperties.ResourceVersion == objProperties.ReflectedVersion)
                {
                    Logger.LogDebug("Skipping {reflectionNsName} - Source {sourceNsName} matches reflected version",
                        objNsName, sourceNsName);
                    return;
                }

                await ResourceReflect(
                    sourceNsName,
                    objNsName,
                    null,
                    obj,
                    false,
                    cancellationToken);

                return;
            }
            //If this is an auto-reflection, ensure it still has a source. reflection will be done when we hit the source
            case { IsReflection: true, IsAutoReflection: true }:
            {
                var sourceNsName = objProperties.Reflects;

                //If the source is known to not exist, drop the reflection
                if (_notFoundCache.ContainsKey(sourceNsName))
                {
                    Logger.LogInformation("Source {sourceNsName} no longer exists. Deleting {reflectionNsName}.",
                        sourceNsName, objNsName);
                    await OnResourceDelete(objNsName);
                    return;
                }


                //Find the source resource
                MirroringProperties sourceProperties;
                if (!_propertiesCache.TryGetValue(sourceNsName, out var props))
                {
                    var sourceResource = await TryResourceGet(sourceNsName, cancellationToken);
                    if (sourceResource is null)
                    {
                        Logger.LogInformation("Source {sourceNsName} no longer exists. Deleting {reflectionNsName}.",
                            sourceNsName, objNsName);
                        await OnResourceDelete(objNsName);
                        return;
                    }

                    sourceProperties = sourceResource.GetMirroringProperties();
                }
                else
                {
                    sourceProperties = props;
                }

                _propertiesCache.AddOrUpdate(sourceNsName, sourceProperties,
                    (_, _) => sourceProperties);
                if (!CanBeAutoReflectedToNamespaceCached(sourceProperties, objNsName.Namespace))
                {
                    Logger.LogInformation(
                        "Source {sourceNsName} no longer permits the auto reflection to {reflectionNsName}. Deleting {reflectionNsName}.",
                        sourceNsName, objNsName,
                        objNsName);
                    await OnResourceDelete(objNsName);
                }

                break;
            }
        }
    }


    private async Task AutoReflectionForSource(NamespacedName sourceNsName, TResource? sourceObj,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Processing auto-reflection source {sourceNsName}", sourceNsName);
        var sourceProperties = _propertiesCache[sourceNsName];

        var autoReflectionList = _autoReflectionCache
            .GetOrAdd(sourceNsName, _ => []);

        var matches = await OnResourceWithNameList(sourceNsName.Name, cancellationToken);
        var namespaces = (await Kubernetes.CoreV1
            .ListNamespaceAsync(cancellationToken: cancellationToken)).Items;

        //Cache namespaces for label selector lookups
        foreach (var ns in namespaces)
            _namespaceCache.AddOrUpdate(ns.Name(), ns, (_, _) => ns);

        foreach (var match in matches)
        {
            var matchProperties = match.GetMirroringProperties();
            _propertiesCache.AddOrUpdate(match.ObjectReference().NamespacedName(),
                _ => matchProperties, (_, _) => matchProperties);
        }

        var namespaceLookup = namespaces.ToDictionary(n => n.Name());

        var toDelete = matches
            .Where(s => s.Namespace() != sourceNsName.Namespace)
            .Where(m => !namespaceLookup.TryGetValue(m.Namespace(), out var ns) ||
                        !sourceProperties.CanBeAutoReflectedToNamespace(ns))
            .Where(m => m.GetMirroringProperties().Reflects == sourceNsName)
            .Select(s => s.NamespacedName())
            .ToList();

        foreach (var reference in toDelete) await OnResourceDelete(reference);

        sourceObj ??= await TryResourceGet(sourceNsName, cancellationToken);
        if (sourceObj is null) return;

        var toCreate = namespaces
            .Where(s => s.Name() != sourceNsName.Namespace)
            .Where(s =>
                matches.All(m => m.Namespace() != s.Name()) &&
                sourceProperties.CanBeAutoReflectedToNamespace(s))
            .Select(s => new NamespacedName(s.Name(), sourceNsName.Name)).ToList();

        var toUpdate = matches
            .Where(s => s.Namespace() != sourceNsName.Namespace)
            .Where(m => !toDelete.Contains(m.NamespacedName()) && !toCreate.Contains(m.NamespacedName()) &&
                        m.GetMirroringProperties().ReflectedVersion != sourceProperties.ResourceVersion &&
                        m.GetMirroringProperties().Reflects == sourceNsName)
            .Select(m => m.NamespacedName()).ToList();

        var toSkip = matches
            .Where(s => s.Namespace() != sourceNsName.Namespace)
            .Where(m =>
                !toDelete.Contains(m.NamespacedName()) &&
                !toCreate.Contains(m.NamespacedName()) &&
                m.GetMirroringProperties().ReflectedVersion == sourceProperties.ResourceVersion &&
                m.GetMirroringProperties().Reflects == sourceNsName)
            .Select(m => m.NamespacedName()).ToList();


        autoReflectionList.Clear();
        foreach (var item in toCreate
                     .Concat(toSkip)
                     .Concat(toUpdate)
                     .ToHashSet())
            autoReflectionList.Add(item);

        foreach (var reflectionNsName in toCreate)
            await ResourceReflect(
                sourceNsName,
                reflectionNsName,
                sourceObj,
                null,
                true,
                cancellationToken);
        foreach (var reflectionRef in toUpdate)
        {
            var reflectionObj = matches.Single(s => s.NamespacedName() == reflectionRef);
            await ResourceReflect(
                sourceNsName,
                reflectionRef,
                sourceObj,
                reflectionObj,
                true,
                cancellationToken);
        }

        Logger.LogInformation(
            "Auto-reflected {sourceNsName} where permitted. " +
            "Created {createdCount} - Updated {updatedCount} - Deleted {deletedCount} - Validated {skippedCount}.",
            sourceNsName, toCreate.Count, toUpdate.Count, toDelete.Count, toSkip.Count);
    }


    private async Task ResourceReflect(NamespacedName sourceNsName, NamespacedName reflectionNsName,
        TResource? sourceObj,
        TResource? reflectionObj, bool autoReflection, CancellationToken cancellationToken)
    {
        if (sourceNsName == reflectionNsName) return;

        Logger.LogDebug("Reflecting {sourceNsName} to {reflectionNsName}", sourceNsName, reflectionNsName);

        TResource source;
        if (sourceObj is null)
        {
            var lookup = await TryResourceGet(sourceNsName, cancellationToken);
            if (lookup is not null)
            {
                source = lookup;
            }
            else
            {
                Logger.LogWarning("Could not update {reflectionNsName} - Source {sourceNsName} could not be found.",
                    reflectionNsName, sourceNsName);
                return;
            }
        }
        else
        {
            source = sourceObj;
        }


        var patchAnnotations = new Dictionary<string, string>
        {
            [Annotations.Reflection.MetaAutoReflects] = autoReflection.ToString(),
            [Annotations.Reflection.Reflects] = sourceNsName.ToString(),
            [Annotations.Reflection.MetaReflectedVersion] = source.Metadata.ResourceVersion,
            [Annotations.Reflection.MetaReflectedAt] =
                JsonConvert.SerializeObject(DateTimeOffset.UtcNow).Replace("\"", string.Empty)
        };


        try
        {
            if (reflectionObj is null)
            {
                var newResource = await OnResourceClone(source);
                newResource.Metadata ??= new V1ObjectMeta();
                newResource.Metadata.Name = reflectionNsName.Name;
                newResource.Metadata.NamespaceProperty = reflectionNsName.Namespace;
                newResource.Metadata.Annotations ??= new Dictionary<string, string>();
                var newResourceAnnotations = newResource.Metadata.Annotations;
                foreach (var patchAnnotation in patchAnnotations)
                    newResourceAnnotations[patchAnnotation.Key] = patchAnnotation.Value;
                newResourceAnnotations[Annotations.Reflection.MetaAutoReflects] = autoReflection.ToString();
                newResourceAnnotations[Annotations.Reflection.Reflects] = sourceNsName.ToString();
                newResourceAnnotations[Annotations.Reflection.MetaReflectedVersion] = source.Metadata.ResourceVersion;
                newResourceAnnotations[Annotations.Reflection.MetaReflectedAt] = DateTimeOffset.UtcNow.ToString("O");

                try
                {
                    await OnResourceCreate(newResource, reflectionNsName.Namespace);
                    Logger.LogInformation("Created {reflectionNsName} as a reflection of {sourceNsName}",
                        reflectionNsName, sourceNsName);
                    return;
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
                {
                    //If resource already exists, set target and fallback to patch
                    reflectionObj = await OnResourceGet(reflectionNsName, cancellationToken);
                }
            }

            if (reflectionObj.GetMirroringProperties().ReflectedVersion == source.Metadata.ResourceVersion)
            {
                Logger.LogDebug("Skipping {reflectionNsName} - Source {sourceNsName} matches reflected version",
                    reflectionNsName, sourceNsName);
                return;
            }


            var patchDoc = new JsonPatchDocument<TResource>([], new JsonPropertyNameContractResolver());
            var annotations = new Dictionary<string, string>(reflectionObj.Metadata.Annotations);
            foreach (var patchAnnotation in patchAnnotations)
                annotations[patchAnnotation.Key] = patchAnnotation.Value;
            patchDoc.Replace(e => e.Metadata.Annotations, annotations);

            await OnResourceConfigurePatch(source, patchDoc);

            var patch = JsonConvert.SerializeObject(patchDoc, Formatting.Indented);
            await OnResourceApplyPatch(new V1Patch(patch, V1Patch.PatchType.JsonPatch), reflectionNsName);
            Logger.LogInformation("Patched {reflectionNsName} as a reflection of {sourceNsName}",
                reflectionNsName, sourceNsName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not reflect {sourceNsName} to {reflectionNsName} due to exception.",
                sourceNsName, reflectionNsName);
        }
    }


    protected abstract Task OnResourceApplyPatch(V1Patch source, NamespacedName refId);
    protected abstract Task OnResourceConfigurePatch(TResource source, JsonPatchDocument<TResource> patchDoc);
    protected abstract Task OnResourceCreate(TResource item, string ns);
    protected abstract Task<TResource> OnResourceClone(TResource sourceResource);
    protected abstract Task OnResourceDelete(NamespacedName resourceId);


    protected abstract Task<TResource[]> OnResourceWithNameList(string itemRefName,
        CancellationToken cancellationToken);

    private async Task<TResource?> TryResourceGet(NamespacedName resourceNsName,
        CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("Retrieving {id}", resourceNsName);
            var resource = await OnResourceGet(resourceNsName, cancellationToken);
            _notFoundCache.TryRemove(resourceNsName, out _);
            return resource;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            Logger.LogDebug("Could not find {nsName}", resourceNsName);
            _notFoundCache.TryAdd(resourceNsName, true);
            return null;
        }
    }

    protected abstract Task<TResource> OnResourceGet(NamespacedName refId, CancellationToken cancellationToken);

    protected virtual Task<bool> OnResourceIgnoreCheck(TResource item) => Task.FromResult(false);

    private void WarnOnInvalidLabelSelectors(NamespacedName sourceNsName, MirroringProperties properties)
    {
        var errors = properties.GetLabelSelectorErrors();
        if (errors.Count == 0)
        {
            _lastWarnedSelectorErrors.TryRemove(sourceNsName, out _);
            return;
        }

        var signature = string.Join("|", errors);
        if (_lastWarnedSelectorErrors.TryGetValue(sourceNsName, out var previous) && previous == signature) return;

        _lastWarnedSelectorErrors[sourceNsName] = signature;
        foreach (var error in errors)
            Logger.LogWarning("Invalid label selector on source {sourceNsName}: {error}", sourceNsName, error);
    }

    internal static bool NamespaceLabelsEqual(V1Namespace a, V1Namespace b) =>
        (a.Metadata?.Labels ?? EmptyLabels).SequenceEqual(b.Metadata?.Labels ?? EmptyLabels);

    private bool CanBeReflectedToNamespaceCached(MirroringProperties properties, string ns)
    {
        if (_namespaceCache.TryGetValue(ns, out var nsObj))
            return properties.CanBeReflectedToNamespace(nsObj);

        // Fail closed: a label selector cannot be evaluated without the namespace object.
        // Falling back to the name-only overload would match empty-pattern sources and allow all namespaces.
        if (!string.IsNullOrEmpty(properties.AllowedNamespacesSelector))
        {
            Logger.LogDebug(
                "Namespace {ns} not in cache; denying reflection because a label selector is configured.", ns);
            return false;
        }

        return properties.CanBeReflectedToNamespace(ns);
    }

    private bool CanBeAutoReflectedToNamespaceCached(MirroringProperties properties, string ns)
    {
        if (_namespaceCache.TryGetValue(ns, out var nsObj))
            return properties.CanBeAutoReflectedToNamespace(nsObj);

        // Fail closed: a label selector cannot be evaluated without the namespace object.
        // Falling back to the name-only overload would match empty-pattern sources and allow all namespaces.
        if (!string.IsNullOrEmpty(properties.AllowedNamespacesSelector) ||
            !string.IsNullOrEmpty(properties.AutoNamespacesSelector))
        {
            Logger.LogDebug(
                "Namespace {ns} not in cache; denying auto-reflection because a label selector is configured.", ns);
            return false;
        }

        return properties.CanBeAutoReflectedToNamespace(ns);
    }
}