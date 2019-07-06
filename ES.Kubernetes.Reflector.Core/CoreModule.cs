using Autofac;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Monitors;

namespace ES.Kubernetes.Reflector.Core
{
    public class CoreModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterGeneric(typeof(BroadcastWatcher<,>));
            builder.RegisterGeneric(typeof(BroadcastWatcher<>));


            builder.RegisterType<SecretsMonitor>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<ConfigMapMonitor>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<CustomResourceDefinitionsMonitor>().AsImplementedInterfaces().SingleInstance();
        }
    }
}