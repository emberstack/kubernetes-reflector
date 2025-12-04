using k8s;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ES.Kubernetes.Reflector.Tests.Fixtures;

public sealed class ReflectorFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly Lock Lock = new();

    public KubernetesClientConfiguration? KubernetesClientConfiguration { get; set; }


    public async ValueTask InitializeAsync() => await Task.CompletedTask;

    // https://github.com/serilog/serilog-aspnetcore/issues/289
    // https://github.com/dotnet/AspNetCore.Docs/issues/26609
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (Lock)
        {
            return base.CreateHost(builder);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("tests");
        builder.ConfigureServices(services =>
        {
            var kubernetesClientConfiguration =
                services.SingleOrDefault(d => d.ServiceType == typeof(KubernetesClientConfiguration));
            if (kubernetesClientConfiguration is not null) services.Remove(kubernetesClientConfiguration);

            services.AddSingleton(s =>
            {
                var config = KubernetesClientConfiguration ??
                             KubernetesClientConfiguration.BuildDefaultConfig();
                config.HttpClientTimeout = TimeSpan.FromMinutes(30);

                return config;
            });
        });
    }
}