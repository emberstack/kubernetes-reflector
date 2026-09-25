using System.Diagnostics;
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
    private static readonly TimeSpan ConsumerShutdownTimeout = TimeSpan.FromSeconds(30);

    protected int WatcherTimeout => options.CurrentValue.Watcher?.Timeout ?? 3600;

    protected int EventHandlerTimeout => options.CurrentValue.Watcher?.EventHandlerTimeout ?? 300;

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

            // Kubernetes namespace names must be valid DNS-1123 labels, which are lowercase-only,
            // so normalizing the configured exclusion patterns to lowercase ensures comparisons
            // against Metadata.NamespaceProperty are consistent without changing semantics.
            var excludedNamespacePatterns = GlobMatcher.ParseGlobPatterns(options.CurrentValue.Watcher?.ExcludedNamespaces?.ToLowerInvariant());
            long namespaceExcludedCount = 0;

            Task? consumerTask = null;
            try
            {
                if (excludedNamespacePatterns.Length > 0)
                    logger.LogInformation(
                        "Requesting {type} resources (excluding namespaces matching: {patterns})",
                        typeof(TResource).Name, options.CurrentValue.Watcher?.ExcludedNamespaces);
                else
                    logger.LogInformation("Requesting {type} resources", typeof(TResource).Name);

                //Read using a separate task so the watcher doesn't get stuck waiting on subscribers to handle the event
                consumerTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var watcherEvent in eventChannel.Reader.ReadAllAsync(cancellationToken)
                                           .ConfigureAwait(false))
                        foreach (var watcherEventHandler in watcherEventHandlers)
                            await HandleEvent(watcherEventHandler, watcherEvent, cancellationToken)
                                .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Expected on session shutdown when cancellation propagates through API calls.
                    }
                    catch (Exception exception)
                    {
                        logger.LogCritical(exception, "Event processing for {type} stopped unexpectedly.",
                            typeof(TResource).Name);
                        throw;
                    }
                }, cancellationToken);

                var watchList = OnGetWatcher(cancellationToken);

                try
                {
                    await foreach (var (type, item) in watchList)
                    {
                        // If event processing stopped, restart the session instead of silently
                        // buffering events that will never be handled.
                        if (consumerTask.IsCompleted)
                        {
                            logger.LogWarning("Event processing for {type} is not running. Restarting session.",
                                typeof(TResource).Name);
                            sessionFaulted = true;
                            break;
                        }

                        // For cluster-scoped resources like V1Namespace, Metadata.NamespaceProperty is null,
                        // so this exclusion check intentionally becomes a no-op and namespace events
                        // continue flowing to support auto-reflection on new namespace creation.
                        if (GlobMatcher.IsNamespaceExcluded(item.Metadata?.NamespaceProperty, excludedNamespacePatterns))
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

                if (consumerTask is not null)
                    try
                    {
                        // Let the in-flight event finish, but never block the next session on it.
                        await consumerTask.WaitAsync(ConsumerShutdownTimeout, stoppingToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        logger.LogWarning("Event processing for {type} did not stop within {timeout}.",
                            typeof(TResource).Name, ConsumerShutdownTimeout);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception)
                    {
                        // Already logged by the consumer.
                        sessionFaulted = true;
                    }

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

    private async Task HandleEvent(IWatcherEventHandler handler, WatcherEvent watcherEvent,
        CancellationToken cancellationToken)
    {
        using var eventTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        eventTimeoutCts.CancelAfter(TimeSpan.FromSeconds(EventHandlerTimeout));
        try
        {
            await handler.Handle(new WatcherEvent
            {
                Item = watcherEvent.Item,
                EventType = watcherEvent.EventType
            }, eventTimeoutCts.Token).ConfigureAwait(false);
        }
        // Only session shutdown may stop event processing; any other failure skips this event.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (eventTimeoutCts.IsCancellationRequested)
                logger.LogError(
                    "Handling {eventType} {type} {namespace}/{name} did not complete within {timeout}s and was canceled.",
                    watcherEvent.EventType, typeof(TResource).Name, watcherEvent.Item?.Metadata?.NamespaceProperty,
                    watcherEvent.Item?.Metadata?.Name, EventHandlerTimeout);
            else
                logger.LogError(exception, "Failed to handle {eventType} {type} {namespace}/{name}.",
                    watcherEvent.EventType, typeof(TResource).Name, watcherEvent.Item?.Metadata?.NamespaceProperty,
                    watcherEvent.Item?.Metadata?.Name);
        }
    }

    protected abstract IAsyncEnumerable<(WatchEventType, TResource)> OnGetWatcher(CancellationToken cancellationToken);

    protected virtual Task<bool> OnResourceIgnoreCheck(TResource item) => Task.FromResult(false);

}
