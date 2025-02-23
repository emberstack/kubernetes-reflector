using System.Diagnostics;
using ES.Kubernetes.Reflector.Core.Configuration;
using ES.Kubernetes.Reflector.Core.Messages;
using k8s;
using k8s.Autorest;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core.Watchers;

public abstract class WatcherBackgroundService<TResource, TResourceList>(
    ILogger logger,
    IMediator mediator,
    IServiceProvider serviceProvider,
    IOptionsMonitor<ReflectorOptions> options)
    : BackgroundService
    where TResource : IKubernetesObject<V1ObjectMeta>
{
    protected readonly ILogger Logger = logger;
    protected readonly IMediator Mediator = mediator;

    protected int WatcherTimeout => options.CurrentValue.Watcher?.Timeout ?? 3600;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sessionStopwatch = new Stopwatch();
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            var sessionFaulted = false;
            sessionStopwatch.Restart();


            try
            {
                Logger.LogInformation("Requesting {type} resources", typeof(TResource).Name);


                using var absoluteTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(WatcherTimeout + 3));
                using var cancellationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, absoluteTimeoutCts.Token);
                using var client = scope.ServiceProvider.GetRequiredService<IKubernetes>();

                using var watcher = OnGetWatcher(client, stoppingToken);
                var watchList = watcher.WatchAsync<TResource, TResourceList>(cancellationToken: cancellationCts.Token);

                await foreach (var (type, item) in watchList)
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
                    ResourceType = typeof(TResource),
                    Faulted = sessionFaulted
                }, stoppingToken);

                if (sessionFaulted) await Task.Delay(5000, stoppingToken);
            }
        }
    }

    protected abstract Task<HttpOperationResponse<TResourceList>> OnGetWatcher(IKubernetes client,
        CancellationToken cancellationToken);
}