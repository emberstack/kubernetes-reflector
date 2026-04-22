using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Watchers.Core;

public abstract class WatcherBackgroundService<TResource, TResourceList>(
    ILogger logger,
    IOptionsMonitor<ReflectorOptions> options,
    IEnumerable<IWatcherEventHandler> watcherEventHandlers,
    IEnumerable<IWatcherClosedHandler> watcherClosedHandlers)
    : BackgroundService
    where TResource : IKubernetesObject<V1ObjectMeta>
{
    protected int WatcherTimeout => options.CurrentValue.Watcher?.Timeout ?? 3600;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sessionStopwatch = new Stopwatch();
        while (!stoppingToken.IsCancellationRequested)
        {
            var sessionFaulted = false;
            sessionStopwatch.Restart();

            using var absoluteTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(WatcherTimeout + 3));
            using var cancellationCts =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, absoluteTimeoutCts.Token);
            var cancellationToken = cancellationCts.Token;

            var eventChannel = Channel.CreateBounded<WatcherEvent>(new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            var excludedNamespacePatterns = ParseGlobPatterns(options.CurrentValue.Watcher?.ExcludedNamespaces);
            long namespaceExcludedCount = 0;
            try
            {
                if (excludedNamespacePatterns.Length > 0)
                    logger.LogInformation(
                        "Requesting {type} resources (excluding namespaces matching: {patterns})",
                        typeof(TResource).Name, options.CurrentValue.Watcher?.ExcludedNamespaces);
                else
                    logger.LogInformation("Requesting {type} resources", typeof(TResource).Name);

                //Read using a separate task so the watcher doesn't get stuck waiting on subscribers to handle the event
                _ = Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var watcherEvent = await eventChannel.Reader.ReadAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var watcherEventHandler in watcherEventHandlers)
                            await watcherEventHandler.Handle(new WatcherEvent
                            {
                                Item = watcherEvent.Item,
                                EventType = watcherEvent.EventType
                            }, cancellationToken);
                    }
                }, cancellationToken);

                var watchList = OnGetWatcher(cancellationToken);

                try
                {
                    await foreach (var (type, item) in watchList)
                    {
                        if (IsNamespaceExcluded(item.Metadata?.NamespaceProperty, excludedNamespacePatterns))
                        {
                            namespaceExcludedCount++;
                            continue;
                        }

                        if (await OnResourceIgnoreCheck(item)) continue;
                        await eventChannel.Writer.WriteAsync(new WatcherEvent
                        {
                            Item = item,
                            EventType = type
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogTrace("Event channel writing canceled.");
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogTrace("Session canceled using token.");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Faulted due to exception.");
                sessionFaulted = true;
            }
            finally
            {
                eventChannel.Writer.Complete();
                while (eventChannel.Reader.TryRead(out _)) ;

                var sessionElapsed = sessionStopwatch.Elapsed;
                sessionStopwatch.Stop();
                if (namespaceExcludedCount > 0)
                    logger.LogInformation(
                        "Session closed. Duration: {duration}. Faulted: {faulted}. Namespace-excluded events: {excluded}.",
                        sessionElapsed, sessionFaulted, namespaceExcludedCount);
                else
                    logger.LogInformation("Session closed. Duration: {duration}. Faulted: {faulted}.",
                        sessionElapsed, sessionFaulted);

                foreach (var handler in watcherClosedHandlers)
                    await handler.Handle(new WatcherClosed
                    {
                        ResourceType = typeof(TResource),
                        Faulted = sessionFaulted
                    }, stoppingToken);
            }
        }
    }

    protected abstract IAsyncEnumerable<(WatchEventType, TResource)> OnGetWatcher(CancellationToken cancellationToken);

    protected virtual Task<bool> OnResourceIgnoreCheck(TResource item) => Task.FromResult(false);

    /// <summary>
    ///     Parses a comma-separated list of glob patterns into compiled Regex objects.
    ///     Supports * (any characters) and ? (single character).
    /// </summary>
    private static Regex[] ParseGlobPatterns(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns)) return [];
        return patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(
                "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.Compiled))
            .ToArray();
    }

    private static bool IsNamespaceExcluded(string? ns, Regex[] patterns)
    {
        if (patterns.Length == 0 || string.IsNullOrEmpty(ns)) return false;
        return patterns.Any(p => p.IsMatch(ns));
    }
}