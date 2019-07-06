using Autofac;

namespace ES.Kubernetes.Reflector.CertManager
{
    public class CertManagerModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<CertificateSecretAnnotator>().AsImplementedInterfaces();
            builder.RegisterType<CertificatesMonitor>().AsImplementedInterfaces().SingleInstance();
        }
    }
}