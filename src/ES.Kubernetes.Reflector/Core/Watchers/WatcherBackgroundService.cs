using System.Diagnostics;
using ES.Kubernetes.Reflector.Core.Messages;
using k8s;
using k8s.Models;
using MediatR;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core.Watchers;

public abstract class WatcherBackgroundService<TResource, TResourceList> : BackgroundService
    where TResource : IKubernetesObject<V1ObjectMeta>
{
    protected readonly IKubernetes Client;
    protected readonly ILogger Logger;
    protected readonly IMediator Mediator;

    protected WatcherBackgroundService(ILogger logger, IMediator mediator, IKubernetes client)
    {
        Logger = logger;
        Mediator = mediator;
        Client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sessionStopwatch = new Stopwatch();
        while (!stoppingToken.IsCancellationRequested)
        {
            var sessionFaulted = false;
            sessionStopwatch.Restart();

            try
            {
                Logger.LogInformation("Requesting {type} resources", typeof(TResource).Name);
                using var watcher = OnGetWatcher(stoppingToken);
                var watchList = watcher.WatchAsync<TResource, TResourceList>();

                await foreach (var (type, item) in watchList
                                   .WithCancellation(stoppingToken))
                    await Mediator.Publish(new WatcherEvent
                    {
                        Item = item,
                        Type = type
                    }, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                Logger.LogInformation("Canceled using token.");
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Faulted due to exception.");
                sessionFaulted = true;
            }
            finally
            {
                var sessionElapsed = sessionStopwatch.Elapsed;
                sessionStopwatch.Stop();
                Logger.LogInformation("Session closed. Duration: {duration}. Faulted: {faulted}.", sessionElapsed,
                    sessionFaulted);

                await Mediator.Publish(new WatcherClosed
                {
                    ResourceType = typeof(V1Secret),
                    Faulted = sessionFaulted
                }, stoppingToken);

                if (sessionFaulted) await Task.Delay(5000, stoppingToken);
            }
        }
    }

    protected abstract Task<HttpOperationResponse<TResourceList>> OnGetWatcher(CancellationToken cancellationToken);
}