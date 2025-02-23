using ES.Kubernetes.Reflector.Core;
using ES.Kubernetes.Reflector.Core.Configuration;
using k8s;
using Microsoft.Extensions.Options;
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

    builder.Host.UseSerilog();
    builder.Host.UseConsoleLifetime();


    builder.Services.AddHttpClient();
    builder.Services.AddOptions();
    builder.Services.AddHealthChecks();
    builder.Services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(void).Assembly));
    builder.Services.AddControllers();

    builder.Services.Configure<ReflectorOptions>(builder.Configuration.GetSection("Reflector"));


    builder.Services.AddSingleton<KubernetesClientConfiguration>(s =>
    {
        var reflectorOptions = s.GetRequiredService<IOptions<ReflectorOptions>>();

        var config = KubernetesClientConfiguration.BuildDefaultConfig();
        config.HttpClientTimeout = TimeSpan.FromMinutes(30);
        if (reflectorOptions.Value.Kubernetes is not null)
        {
            config.SkipTlsVerify = 
                reflectorOptions.Value.Kubernetes.SkipTlsVerify.GetValueOrDefault(false);
        }
        return config;
    });
    
    builder.Services.AddSingleton<IKubernetes>(s =>
        new Kubernetes(s.GetRequiredService<KubernetesClientConfiguration>()));
    
    builder.Services.AddHostedService<NamespaceWatcher>();
    builder.Services.AddHostedService<SecretWatcher>();
    builder.Services.AddHostedService<ConfigMapWatcher>();
    
    builder.Services.AddSingleton<SecretMirror>();
    builder.Services.AddSingleton<ConfigMapMirror>();

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