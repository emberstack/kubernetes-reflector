using Autofac;
using Autofac.Extensions.DependencyInjection;
using ES.Kubernetes.Reflector.Core;
using ES.Kubernetes.Reflector.Core.Configuration;
using k8s;
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
    builder.Services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(void).Assembly));
    builder.Services.AddControllers();

    builder.Services.AddSingleton<KubernetesClientConfiguration>(_ =>
    {
        var config = KubernetesClientConfiguration.BuildDefaultConfig();
        config.HttpClientTimeout = TimeSpan.FromMinutes(30);
        return config;
    });
   

    builder.Services.AddSingleton<IKubernetes>(s =>
        new Kubernetes(s.GetRequiredService<KubernetesClientConfiguration>()));

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