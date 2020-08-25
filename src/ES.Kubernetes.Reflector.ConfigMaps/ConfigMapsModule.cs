using Autofac;
using ES.Kubernetes.Reflector.Core.Health;

namespace ES.Kubernetes.Reflector.ConfigMaps
{
    public class ConfigMapsModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<ConfigMapMirror>().AsImplementedInterfaces().AsSelf().SingleInstance();
            builder.AddHealthCheck<ConfigMapMirror>();
        }
    }
}