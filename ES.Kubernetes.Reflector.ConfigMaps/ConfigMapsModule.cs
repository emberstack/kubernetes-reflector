using Autofac;
using ES.Kubernetes.Reflector.Core.Health;

namespace ES.Kubernetes.Reflector.ConfigMaps
{
    public class ConfigMapsModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<Mirror>().AsImplementedInterfaces().AsSelf().SingleInstance();
            builder.AddHealthCheck<Mirror>();
        }
    }
}