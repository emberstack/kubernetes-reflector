using Autofac;

namespace ES.Kubernetes.Reflector.ConfigMaps
{
    public class ConfigMapsModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<Mirror>().AsImplementedInterfaces().SingleInstance();
        }
    }
}