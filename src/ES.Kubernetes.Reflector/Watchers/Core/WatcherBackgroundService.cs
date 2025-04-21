using System.Diagnostics;
using System.Threading.Channels;
using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Autorest;
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

            var eventChannel = Channel.CreateBounded<WatcherEvent>(new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

            try
            {
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

                using var watcher = OnGetWatcher(cancellationToken);
                var watchList = watcher.WatchAsync<TResource, TResourceList>(cancellationToken: cancellationToken);

                try
                {
                    await foreach (var (type, item) in watchList)
                    {
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
                logger.LogInformation("Session closed. Duration: {duration}. Faulted: {faulted}.", sessionElapsed,
                    sessionFaulted);

                foreach (var handler in watcherClosedHandlers)
                    await handler.Handle(new WatcherClosed
                    {
                        ResourceType = typeof(TResource),
                        Faulted = sessionFaulted
                    }, stoppingToken);
            }
        }
    }

    protected abstract Task<HttpOperationResponse<TResourceList>> OnGetWatcher(CancellationToken cancellationToken);

    protected virtual Task<bool> OnResourceIgnoreCheck(TResource item) => Task.FromResult(false);
}