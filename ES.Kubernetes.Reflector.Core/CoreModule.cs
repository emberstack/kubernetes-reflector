using System;
using Autofac;
using ES.Kubernetes.Reflector.Core.Monitoring;
using ES.Kubernetes.Reflector.Core.Monitors;
using k8s;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector.Core
{
    public class CoreModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterGeneric(typeof(BroadcastWatcher<,>));
            builder.RegisterGeneric(typeof(BroadcastWatcher<>));


            builder.Register(s =>
            {
                var logger = s.Resolve<ILogger<CoreModule>>();
                var inCluster = KubernetesClientConfiguration.IsInCluster();
                logger.LogDebug("Building client configuration. Client is in cluster: '{inCluster}'", inCluster);

                return inCluster
                    ? KubernetesClientConfiguration.InClusterConfig()
                    : KubernetesClientConfiguration.BuildConfigFromConfigFile();
            }).As<KubernetesClientConfiguration>().SingleInstance();

            builder.Register(s => new k8s.Kubernetes(s.Resolve<KubernetesClientConfiguration>())
                {HttpClient = {Timeout = TimeSpan.FromMinutes(60)}}).As<k8s.Kubernetes>().As<IKubernetes>();


            builder.RegisterType<SecretsMonitor>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<ConfigMapMonitor>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<CustomResourceDefinitionsMonitor>().AsImplementedInterfaces().SingleInstance();
        }
    }
}