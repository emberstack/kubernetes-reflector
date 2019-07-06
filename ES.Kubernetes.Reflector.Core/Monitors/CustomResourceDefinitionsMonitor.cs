using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Monitoring;
using k8s.Models;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector.Core.Monitors
{
    public class CustomResourceDefinitionsMonitor : IHostedService,
        IRequestHandler<HealthCheckRequest<CustomResourceDefinitionsMonitor>, bool>
    {
        private readonly ILogger<CustomResourceDefinitionsMonitor> _logger;
        private readonly BroadcastWatcher<V1beta1CustomResourceDefinition> _watcher;

        public CustomResourceDefinitionsMonitor(
            ILogger<CustomResourceDefinitionsMonitor> logger,
            BroadcastWatcher<V1beta1CustomResourceDefinition> watcher)
        {
            _logger = logger;
            _watcher = watcher;
            _watcher.OnStateChanged = async (sender, update) =>
            {
                switch (update.State)
                {
                    case BroadcastWatcherState.Closed:
                        _logger.LogDebug("Watcher {state}", update.State);
                        await sender.Start();
                        break;
                    case BroadcastWatcherState.Faulted:
                        _logger.LogError(update.Exception, "Watcher {state}", update.State);
                        break;
                    default:
                        _logger.LogDebug("Watcher {state}", update.State);
                        break;
                }
            };
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting");
            _watcher.RequestFactory = async client =>
                await client.ListCustomResourceDefinitionWithHttpMessagesAsync(watch: true);
            await _watcher.Start();
            _logger.LogInformation("Started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Stopping");
            await _watcher.Stop();
            _logger.LogInformation("Stopped");
        }

        public Task<bool> Handle(HealthCheckRequest<CustomResourceDefinitionsMonitor> request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(!_watcher.IsFaulted);
        }
    }
}