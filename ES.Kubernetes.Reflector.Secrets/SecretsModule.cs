using Autofac;
using ES.Kubernetes.Reflector.Core.Health;

namespace ES.Kubernetes.Reflector.Secrets
{
    public class SecretsModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<Mirror>().AsImplementedInterfaces().AsSelf().SingleInstance();
            builder.AddHealthCheck<Mirror>();
        }
    }
}