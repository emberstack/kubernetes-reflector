using System;
using System.Reflection;
using Autofac;
using ES.Kubernetes.Reflector.CertManager;
using ES.Kubernetes.Reflector.ConfigMaps;
using ES.Kubernetes.Reflector.Core;
using ES.Kubernetes.Reflector.Secrets;
using k8s;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            services.AddMediatR(Assembly.GetExecutingAssembly());

            services.AddHealthChecks()
                .AddCheck<CoreHealthCheck>("Core")
                .AddCheck<CertManagerHealthCheck>("Extensions.CertManager");

            services.AddSingleton(s =>
            {
                var logger = s.GetRequiredService<ILogger<Startup>>();
                var inCluster = KubernetesClientConfiguration.IsInCluster();
                logger.LogDebug("Building client configuration. Client is in cluster: '{inCluster}'", inCluster);

                return inCluster
                    ? KubernetesClientConfiguration.InClusterConfig()
                    : KubernetesClientConfiguration.BuildConfigFromConfigFile();
            });

            services.AddTransient<IKubernetes>(s =>
                new k8s.Kubernetes(s.GetRequiredService<KubernetesClientConfiguration>())
                    {HttpClient = {Timeout = TimeSpan.FromMinutes(60)}});
        }


        // ReSharper disable once UnusedMember.Global
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterModule<CoreModule>();
            builder.RegisterModule<SecretsModule>();
            builder.RegisterModule<ConfigMapsModule>();
            builder.RegisterModule<CertManagerModule>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            var forwardingOptions = new ForwardedHeadersOptions
            {
                RequireHeaderSymmetry = false,
                ForwardedHeaders = ForwardedHeaders.All,
                ForwardLimit = null
            };
            forwardingOptions.KnownNetworks.Clear();
            forwardingOptions.KnownProxies.Clear();
            app.UseForwardedHeaders(forwardingOptions);

            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

            app.UseHealthChecks("/healthz");

            app.UseMvc();
        }
    }
}