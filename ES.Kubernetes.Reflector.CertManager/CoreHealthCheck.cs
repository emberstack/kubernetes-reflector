using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Events;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class CertManagerHealthCheck : IHealthCheck
    {
        private readonly IMediator _mediator;

        public CertManagerHealthCheck(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var checkMessages = new List<IRequest<bool>>
            {
                new HealthCheckRequest<CertificatesMonitor>()
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
                ? HealthCheckResult.Healthy("CertManager extension is healthy")
                : HealthCheckResult.Unhealthy("CertManager extension is unhealthy");
        }
    }
}