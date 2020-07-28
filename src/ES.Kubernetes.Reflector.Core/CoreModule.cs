using Autofac;
using ES.Kubernetes.Reflector.Core.Monitoring;

namespace ES.Kubernetes.Reflector.Core
{
    public class CoreModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterGeneric(typeof(ManagedWatcher<,>));
            builder.RegisterGeneric(typeof(ManagedWatcher<,,>));
        }
    }
}