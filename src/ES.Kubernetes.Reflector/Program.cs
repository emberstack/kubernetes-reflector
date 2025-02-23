using ES.FX.Hosting.Lifetime;
using ES.FX.Ignite.Hosting;
using ES.FX.Ignite.OpenTelemetry.Exporter.Seq.Hosting;
using ES.FX.Ignite.Serilog.Hosting;
using ES.FX.Serilog.Lifetime;
using ES.Kubernetes.Reflector.Core;
using ES.Kubernetes.Reflector.Core.Configuration;
using k8s;
using Microsoft.Extensions.Options;

return await ProgramEntry.CreateBuilder(args).UseSerilog().Build().RunAsync(async _ =>
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();

    builder.Configuration.AddEnvironmentVariables("ES_");

    builder.Ignite();
    builder.IgniteSerilog();
    builder.IgniteSeqOpenTelemetryExporter();
    builder.Services.AddMediatR(config =>
        config.RegisterServicesFromAssembly(typeof(Program).Assembly));

    builder.Services.Configure<ReflectorOptions>(builder.Configuration.GetSection(nameof(ES.Kubernetes.Reflector)));

    builder.Services.AddTransient(s =>
    {
        var reflectorOptions = s.GetRequiredService<IOptions<ReflectorOptions>>();

        var config = KubernetesClientConfiguration.BuildDefaultConfig();
        if (reflectorOptions.Value.Kubernetes is not null)
            config.SkipTlsVerify =
                reflectorOptions.Value.Kubernetes.SkipTlsVerify.GetValueOrDefault(false);
        return config;
    });


    builder.Services.AddTransient<IKubernetes>(s =>
        new Kubernetes(s.GetRequiredService<KubernetesClientConfiguration>()));

    builder.Services.AddHostedService<NamespaceWatcher>();
    builder.Services.AddHostedService<SecretWatcher>();
    builder.Services.AddHostedService<ConfigMapWatcher>();

    builder.Services.AddSingleton<SecretMirror>();
    builder.Services.AddSingleton<ConfigMapMirror>();


    var app = builder.Build();
    app.Ignite();
    await app.RunAsync();
    return 0;
});