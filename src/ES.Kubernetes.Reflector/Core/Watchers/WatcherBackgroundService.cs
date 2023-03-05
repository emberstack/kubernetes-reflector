using System.Diagnostics;
using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Messages;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core.Watchers;

public abstract class WatcherBackgroundService<TResource, TResourceList> : BackgroundService
    where TResource : IKubernetesObject<V1ObjectMeta>
{
    private readonly IOptionsMonitor<ReflectorOptions> _options;
    protected readonly IKubernetes Client;
    protected readonly ILogger Logger;
    protected readonly IMediator Mediator;

    protected WatcherBackgroundService(ILogger logger, IMediator mediator, IKubernetes client,
        IOptionsMonitor<ReflectorOptions> options)
    {
        Logger = logger;
        Mediator = mediator;
        Client = client;
        _options = options;
    }

    protected int? WatcherTimeout => _options.CurrentValue.Watcher?.Timeout;

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
                var watchList = watcher.WatchAsync<TResource, TResourceList>(cancellationToken: stoppingToken);

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