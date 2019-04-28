using System;
using ES.Kubernetes.Reflector.Business;
using ES.Kubernetes.Reflector.Services;
using k8s;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ES.Kubernetes.Reflector
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger();
            Configuration = configuration;
        }


        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);


            services.AddSingleton(s =>
            {
                var logger = s.GetRequiredService<ILogger<Startup>>();
                var inCluster = KubernetesClientConfiguration.IsInCluster();
                logger.LogDebug("Building client configuration. Client is in cluster: '{inCluster}'", inCluster);

                return inCluster
                    ? KubernetesClientConfiguration.InClusterConfig()
                    : KubernetesClientConfiguration.BuildConfigFromConfigFile();
            });

            services.AddSingleton<IKubernetes>(s =>
            {
                var logger = s.GetRequiredService<ILogger<Startup>>();
                logger.LogDebug("Initializing Kubernetes client");
                return new k8s.Kubernetes(s.GetRequiredService<KubernetesClientConfiguration>())
                { HttpClient = { Timeout = TimeSpan.FromMinutes(60) } };
            });

            services.AddTransient<SecretsMonitor>();
            services.AddTransient<ConfigMapMonitor>();
            services.AddTransient<CustomResourceDefinitionMonitor>();
            services.AddTransient<CertManagerCertificatesMonitor>();

            services.AddHostedService<SecretsMirror>();
            services.AddHostedService<ConfigMapMirror>();
            services.AddHostedService<CertManagerMirror>();
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

            app.UseMvc();
        }
    }
}