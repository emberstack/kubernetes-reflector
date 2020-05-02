using System.Net.Http;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ES.Kubernetes.Reflector.Host
{
    public class Startup
    {

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddOptions();
            services.AddHealthChecks();
            services.AddMediatR(Assembly.GetExecutingAssembly());

            services.AddControllers();
        }


        // This method gets called by convention to configure Autofac. 
        // ReSharper disable once UnusedMember.Global
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.Register(c => c.Resolve<IHttpClientFactory>().CreateClient()).AsSelf();

            builder.RegisterModule<CoreModule>();
            builder.RegisterModule<SecretsModule>();
            builder.RegisterModule<ConfigMapsModule>();
            builder.RegisterModule<CertManagerModule>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        // ReSharper disable once UnusedMember.Global
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
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

            app.UseHealthChecks("/healthz");


            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
