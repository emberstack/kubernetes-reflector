using System.Reflection;
using Autofac;
using ES.Kubernetes.Reflector.CertManager;
using ES.Kubernetes.Reflector.ConfigMaps;
using ES.Kubernetes.Reflector.Core;
using ES.Kubernetes.Reflector.Secrets;
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
        private readonly ILogger<Startup> _logger;

        public Startup(IConfiguration configuration, ILogger<Startup> logger)
        {
            _logger = logger;
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }


        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            services.AddMediatR(Assembly.GetExecutingAssembly());

            services.AddHealthChecks();
        }


        // ReSharper disable once UnusedMember.Global
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterModule<CoreModule>();
            builder.RegisterModule<SecretsModule>();
            builder.RegisterModule<ConfigMapsModule>();

            var certManagerEnabled = bool.Parse(Configuration["Reflector:Extensions:CertManager:Enabled"]);
            _logger.LogInformation("CertManager extension enabled: {certManagerEnabled}", certManagerEnabled);
            if (certManagerEnabled) builder.RegisterModule<CertManagerModule>();
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