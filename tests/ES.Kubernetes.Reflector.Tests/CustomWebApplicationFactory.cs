using ES.Kubernetes.Reflector.Configuration;
using k8s;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Testcontainers.K3s;

namespace ES.Kubernetes.Reflector.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly K3sContainer _container = new K3sBuilder()
        .WithImage("rancher/k3s:v1.26.2-k3s1")
        .Build();
    private static readonly Lock Lock = new();
    
    /// <summary>
    /// https://github.com/serilog/serilog-aspnetcore/issues/289
    /// https://github.com/dotnet/AspNetCore.Docs/issues/26609
    /// There is a problem with using Serilog's "CreateBootstrapLogger" when trying to initialize a web host.
    /// This is because in tests, multiple hosts are created in parallel, and Serilog's static logger is not thread-safe.
    /// The way around this without touching the host code is to lock the creation of the host to a single thread at a time.
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (Lock)
            return base.CreateHost(builder);
    }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var kubeConfigContent = _container.GetKubeconfigAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(kubeConfigContent))
        {
            throw new InvalidOperationException("Kubeconfig content is empty");
        }
        
        builder.ConfigureServices(services =>
        {
            //  remove the existing KubernetesClientConfiguration and IKubernetes registrations
            var kubernetesClientConfiguration = services.SingleOrDefault(
                d => d.ServiceType == typeof(KubernetesClientConfiguration));
            if (kubernetesClientConfiguration is not null)
            {
                services.Remove(kubernetesClientConfiguration);
            }
            
            services.AddSingleton(s =>
            {
                var reflectorOptions = s.GetRequiredService<IOptions<ReflectorOptions>>();

                // create config file on disk file from _kubeConfigContent
                var tempFile = Path.GetTempFileName();
                File.WriteAllText(tempFile, kubeConfigContent);

                var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(tempFile);
                config.HttpClientTimeout = TimeSpan.FromMinutes(30);

                return config;
            });

            services.AddSingleton<IKubernetes>(s =>
                new k8s.Kubernetes(s.GetRequiredService<KubernetesClientConfiguration>()));
        });
    }

    public Task InitializeAsync() 
        => _container.StartAsync();

    public new Task DisposeAsync() 
        => _container.DisposeAsync().AsTask();
}