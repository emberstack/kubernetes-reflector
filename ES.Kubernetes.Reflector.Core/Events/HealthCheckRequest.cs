using MediatR;

namespace ES.Kubernetes.Reflector.Core.Events
{
    public class HealthCheckRequest<T> : IRequest<bool>, IRequest<Unit>
    {
    }
}