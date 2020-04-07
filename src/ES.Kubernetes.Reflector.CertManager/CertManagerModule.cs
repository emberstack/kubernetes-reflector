using Autofac;
using ES.Kubernetes.Reflector.Core.Health;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class CertManagerModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<SecretEtcher>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<Monitor>().AsImplementedInterfaces().AsSelf().SingleInstance();
            builder.AddHealthCheck<Monitor>();
        }
    }
}