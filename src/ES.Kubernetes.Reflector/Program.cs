using ES.FX.Additions.KubernetesClient.Models.Extensions;
using ES.FX.Additions.Serilog.Lifetime;
using ES.FX.Hosting.Lifetime;
using ES.FX.Ignite.Configuration;
using ES.FX.Ignite.Hosting;
using ES.FX.Ignite.KubernetesClient.Hosting;
using ES.FX.Ignite.OpenTelemetry.Exporter.Seq.Hosting;
using ES.FX.Ignite.Serilog.Hosting;
using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Metrics;
using ES.Kubernetes.Reflector.Mirroring;
using ES.Kubernetes.Reflector.Watchers;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s.Models;
using OpenTelemetry;
using OpenTelemetry.Metrics;

return await ProgramEntry.CreateBuilder(args).UseSerilog().Build().RunAsync(async _ =>
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddEnvironmentVariables("ES_");

    builder.Logging.ClearProviders();
    IgniteSettings? igniteSettings = null;
    builder.Ignite(settings => igniteSettings = settings);
    builder.IgniteSerilog(config =>
        config.Destructure.ByTransforming<V1ObjectReference>(v => v.NamespacedName()));
    builder.IgniteSeqOpenTelemetryExporter();
    builder.IgniteKubernetesClient();

    if (igniteSettings is { OpenTelemetry.Enabled: true })
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(ReflectorMetrics.MeterName);
                metrics.AddPrometheusExporter();
            });

    builder.Services.Configure<ReflectorOptions>(builder.Configuration.GetSection(nameof(ES.Kubernetes.Reflector)));

    builder.Services.AddHostedService<NamespaceWatcher>();
    builder.Services.AddHostedService<SecretWatcher>();
    builder.Services.AddHostedService<ConfigMapWatcher>();

    builder.Services.AddSingleton<ReflectorMetrics>();

    builder.Services.AddSingleton<SecretMirror>();
    builder.Services.AddSingleton<ConfigMapMirror>();

    builder.Services.AddSingleton<IWatcherEventHandler>(sp => sp.GetRequiredService<SecretMirror>());
    builder.Services.AddSingleton<IWatcherClosedHandler>(sp => sp.GetRequiredService<SecretMirror>());

    builder.Services.AddSingleton<IWatcherEventHandler>(sp => sp.GetRequiredService<ConfigMapMirror>());
    builder.Services.AddSingleton<IWatcherClosedHandler>(sp => sp.GetRequiredService<ConfigMapMirror>());

    var app = builder.Build();
    app.Ignite();
    if (igniteSettings is { OpenTelemetry.Enabled: true })
        app.MapPrometheusScrapingEndpoint();
    await app.RunAsync();
    return 0;
});

public partial class Program;