using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace ES.Kubernetes.Reflector.Metrics;

/// <summary>
///     Defines the application metrics describing the state and activity of the Reflector.
///     The instruments are published under the <see cref="MeterName" /> meter, which is registered
///     with the OpenTelemetry meter provider.
/// </summary>
public class ReflectorMetrics
{
    public const string MeterName = "ES.Kubernetes.Reflector";

    private readonly ConcurrentBag<Func<ReflectionState>> _stateProviders = [];

    private readonly Counter<long> _reflectionCounter;
    private readonly Counter<long> _reflectionErrorCounter;
    private readonly Counter<long> _watcherSessionCounter;
    private readonly Counter<long> _watcherExcludedEventCounter;

    public ReflectorMetrics()
    {
        var meter = new Meter(MeterName);

        _reflectionCounter = meter.CreateCounter<long>("reflector.reflections",
            description: "Number of reflection operations performed on target resources");
        _reflectionErrorCounter = meter.CreateCounter<long>("reflector.reflection_errors",
            description: "Number of reflection operations that failed with an error");
        _watcherSessionCounter = meter.CreateCounter<long>("reflector.watcher_sessions",
            description: "Number of watcher sessions started");
        _watcherExcludedEventCounter = meter.CreateCounter<long>("reflector.watcher_excluded_events",
            description: "Number of watcher events skipped because the namespace matched the exclusion list");

        meter.CreateObservableGauge<long>("reflector.tracked_sources",
            CollectTrackedSources,
            description: "Number of auto-reflection sources currently tracked");
        meter.CreateObservableGauge<long>("reflector.tracked_reflections",
            CollectTrackedReflections,
            description: "Number of reflections currently tracked");
    }


    /// <summary>
    ///     Records a completed reflection operation.
    /// </summary>
    /// <param name="kind">The kind of the reflected resource (e.g. <c>secret</c>, <c>configmap</c>)</param>
    /// <param name="result">The result of the operation: <c>created</c>, <c>patched</c> or <c>deleted</c></param>
    public void AddReflection(string kind, string result) => _reflectionCounter.Add(1,
        new KeyValuePair<string, object?>("kind", kind),
        new KeyValuePair<string, object?>("result", result));

    /// <summary>
    ///     Records a reflection operation that failed with an exception.
    /// </summary>
    /// <param name="kind">The kind of the reflected resource (e.g. <c>secret</c>, <c>configmap</c>)</param>
    public void AddReflectionError(string kind) => _reflectionErrorCounter.Add(1,
        new KeyValuePair<string, object?>("kind", kind));

    /// <summary>
    ///     Records a closed watcher session.
    /// </summary>
    /// <param name="resourceType">The type of the watched resource (e.g. <c>secret</c>, <c>namespace</c>)</param>
    /// <param name="faulted">Whether the session ended with an error</param>
    public void AddWatcherSession(string resourceType, bool faulted) => _watcherSessionCounter.Add(1,
        new KeyValuePair<string, object?>("type", resourceType),
        new KeyValuePair<string, object?>("faulted", faulted ? "true" : "false"));

    /// <summary>
    ///     Records a watcher event skipped because the namespace matched the exclusion list.
    /// </summary>
    /// <param name="resourceType">The type of the watched resource (e.g. <c>secret</c>, <c>namespace</c>)</param>
    public void AddWatcherExcludedEvent(string resourceType) => _watcherExcludedEventCounter.Add(1,
        new KeyValuePair<string, object?>("type", resourceType));

    /// <summary>
    ///     Registers a state provider used to compute the tracked source and reflection gauges.
    /// </summary>
    /// <param name="provider">Function returning a snapshot of the mirror caches</param>
    public void RegisterReflectionState(Func<ReflectionState> provider) => _stateProviders.Add(provider);

    /// <summary>
    ///     Returns the normalized kind name for a Kubernetes resource type (e.g. <c>V1Secret</c> to <c>secret</c>).
    /// </summary>
    public static string ResourceKind(Type type)
    {
        var name = type.Name;
        if (name.StartsWith("V1")) name = name[2..];
        return name.ToLowerInvariant();
    }


    private IEnumerable<Measurement<long>> CollectTrackedSources()
    {
        foreach (var provider in _stateProviders)
        {
            ReflectionState state;
            try
            {
                state = provider();
            }
            catch
            {
                //The caches are updated concurrently; a snapshot taken mid-mutation is skipped
                continue;
            }

            yield return new Measurement<long>(state.Sources,
                new KeyValuePair<string, object?>("kind", state.Kind));
        }
    }

    private IEnumerable<Measurement<long>> CollectTrackedReflections()
    {
        foreach (var provider in _stateProviders)
        {
            ReflectionState state;
            try
            {
                state = provider();
            }
            catch
            {
                //The caches are updated concurrently; a snapshot taken mid-mutation is skipped
                continue;
            }

            yield return new Measurement<long>(state.DirectReflections,
                new KeyValuePair<string, object?>("kind", state.Kind),
                new KeyValuePair<string, object?>("type", "direct"));
            yield return new Measurement<long>(state.AutoReflections,
                new KeyValuePair<string, object?>("kind", state.Kind),
                new KeyValuePair<string, object?>("type", "auto"));
        }
    }
}

/// <summary>
///     Snapshot of a mirror's internal caches, used to compute the tracked source and reflection gauges.
/// </summary>
public sealed record ReflectionState(string Kind, long Sources, long DirectReflections, long AutoReflections);
