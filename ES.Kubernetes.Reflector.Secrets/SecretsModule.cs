using Autofac;

namespace ES.Kubernetes.Reflector.Secrets
{
    public class SecretsModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<Mirror>().AsImplementedInterfaces().SingleInstance();
        }
    }
}