using System;
using Autofac;
using ES.Kubernetes.Reflector.Core.Monitoring;
using k8s;
using Microsoft.Extensions.Logging;

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