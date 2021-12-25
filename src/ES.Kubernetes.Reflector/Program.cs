using Autofac;
using Autofac.Extensions.DependencyInjection;
using ES.Kubernetes.Reflector.Core;
using ES.Kubernetes.Reflector.Core.Configuration;
using k8s;
using MediatR;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("reflector.logging.json")
        .AddEnvironmentVariables("ES_")
        .AddCommandLine(args)
        .Build())
    .CreateLogger();


try
{
    Log.Information("Starting host");

    var builder = WebApplication.CreateBuilder(args);
    builder.Environment.EnvironmentName =
        Environment.GetEnvironmentVariable($"{nameof(ES)}_{nameof(Environment)}") ??
        Environments.Production;

    builder.Configuration.AddJsonFile("appsettings.json", false, true);
    builder.Configuration.AddJsonFile("reflector.logging.json");
    builder.Configuration.AddEnvironmentVariables("ES_");
    builder.Configuration.AddCommandLine(args);

    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
    builder.Host.UseSerilog();
    builder.Host.UseConsoleLifetime();


    builder.Services.AddHttpClient();
    builder.Services.AddOptions();
    builder.Services.AddHealthChecks();
    builder.Services.AddMediatR(typeof(void).Assembly);
    builder.Services.AddControllers();

    builder.Services.AddSingleton(KubernetesClientConfiguration.BuildDefaultConfig());
    builder.Services.AddScoped<IKubernetes>(c =>
        new Kubernetes(c.GetRequiredService<KubernetesClientConfiguration>()));

    builder.Services.AddHttpClient(nameof(IKubernetes))
        .AddTypedClient<IKubernetes>((httpClient, s) => new Kubernetes(
            s.GetRequiredService<KubernetesClientConfiguration>(),
            httpClient))
        .ConfigurePrimaryHttpMessageHandler(s =>
            s.GetRequiredService<KubernetesClientConfiguration>().CreateDefaultHttpClientHandler());

    builder.Services.Configure<ReflectorOptions>(builder.Configuration.GetSection("Reflector"));


    builder.Host.ConfigureContainer((ContainerBuilder container) =>
    {
        container.Register(c => c.Resolve<IHttpClientFactory>().CreateClient()).AsSelf();

        container.RegisterType<NamespaceWatcher>().AsImplementedInterfaces().SingleInstance();

        container.RegisterType<SecretWatcher>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<SecretMirror>().AsImplementedInterfaces().SingleInstance();

        container.RegisterType<ConfigMapWatcher>().AsImplementedInterfaces().SingleInstance();
        container.RegisterType<ConfigMapMirror>().AsImplementedInterfaces().SingleInstance();
    });

    builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(25080); });


    var app = builder.Build();

    if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

    app.UseStaticFiles();
    app.UseRouting();
    app.UseHealthChecks("/healthz");
    app.UseAuthorization();

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}