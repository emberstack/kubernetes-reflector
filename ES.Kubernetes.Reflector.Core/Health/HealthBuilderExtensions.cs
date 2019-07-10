using System.Collections.Generic;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ES.Kubernetes.Reflector.Core.Health
{
    public static class HealthBuilderExtensions
    {

        public static ContainerBuilder AddHealthCheck<T>(this ContainerBuilder builder, HealthStatus? failureStatus = null, IEnumerable<string> tags = null) where T : IHealthCheck
        {
            builder.AddHealthCheck<T>(typeof(T).FullName, failureStatus, tags);
            return builder;
        }


        public static ContainerBuilder AddHealthCheck<T>(this ContainerBuilder builder, string name, HealthStatus? failureStatus = null, IEnumerable<string> tags = null) where T : IHealthCheck
        {
            builder.AddHealthCheck(new HealthCheckRegistration(name, s => 
                ActivatorUtilities.GetServiceOrCreateInstance<T>(s), failureStatus, tags));
            return builder;
        }


        public static ContainerBuilder AddHealthCheck(this ContainerBuilder builder, HealthCheckRegistration registration)
        {
            builder.RegisterInstance(new ConfigureOptions<HealthCheckServiceOptions>(
                    options => options.Registrations.Add(registration)))
                .As<IConfigureOptions<HealthCheckServiceOptions>>()
                .SingleInstance();
            return builder;
        }
    }
}
