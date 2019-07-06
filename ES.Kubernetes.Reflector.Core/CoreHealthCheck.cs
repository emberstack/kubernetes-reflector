using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Monitors;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ES.Kubernetes.Reflector.Core
{
    public class CoreHealthCheck : IHealthCheck
    {
        private readonly IMediator _mediator;

        public CoreHealthCheck(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var checkMessages = new List<IRequest<bool>>
            {
                new HealthCheckRequest<SecretsMonitor>(),
                new HealthCheckRequest<ConfigMapMonitor>(),
                new HealthCheckRequest<CustomResourceDefinitionsMonitor>()
            };

            var healthy = true;
            foreach (var checkMessage in checkMessages)
            {
                var result = await _mediator.Send(checkMessage, cancellationToken);
                if (!result)
                {
                    healthy = false;
                    break;
                }
            }

            return healthy
                ? HealthCheckResult.Healthy("Core is healthy")
                : HealthCheckResult.Unhealthy("Core is unhealthy");
        }
    }
}