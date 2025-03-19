using k8s;
using k8s.Models;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using ES.FX.Ignite.Configuration;
using k8s.Autorest;

namespace ES.Kubernetes.Reflector.Tests;

public abstract class BaseIntegrationTest : IClassFixture<CustomWebApplicationFactory>
{
    protected readonly HttpClient Client;
    protected readonly IKubernetes K8SClient;
    protected readonly ResiliencePipeline<bool> Pipeline;
    protected readonly IgniteSettings IgniteSettings;

    protected BaseIntegrationTest(CustomWebApplicationFactory factory)
    {
        Client = factory.CreateClient();
        var scope = factory.Services.CreateScope();
        K8SClient = scope.ServiceProvider.GetRequiredService<IKubernetes>();
        IgniteSettings = scope.ServiceProvider.GetRequiredService<IgniteSettings>();

        // Polly retry and timeout policy to fetch replicated resources
        Pipeline = new ResiliencePipelineBuilder<bool>()
            .AddRetry(new RetryStrategyOptions<bool>
            {
                ShouldHandle = new PredicateBuilder<bool>()
                    .Handle<HttpOperationException>(ex => 
                        ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    .HandleResult(false),
                MaxRetryAttempts = 5,
                Delay = TimeSpan.FromSeconds(2),
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }

    protected async Task<V1Namespace?> CreateNamespaceAsync(string name)
    {
        var ns = new V1Namespace
        {
            ApiVersion = V1Namespace.KubeApiVersion,
            Kind = V1Namespace.KubeKind,
            Metadata = new V1ObjectMeta
            {
                Name = name
            }
        };

         return await K8SClient.CoreV1.CreateNamespaceAsync(ns);
    }

    protected async Task<V1ConfigMap?> CreateConfigMapAsync(
        string configMapName,
        IDictionary<string, string> data,
        string destinationNamespace,
        ReflectorAnnotations reflectionAnnotations)
    {
        var configMap = new V1ConfigMap
        {
            ApiVersion = V1ConfigMap.KubeApiVersion,
            Kind = V1ConfigMap.KubeKind,
            Metadata = new V1ObjectMeta
            {
                Name = configMapName,
                NamespaceProperty = destinationNamespace,
                Annotations = reflectionAnnotations.Build()
            },
            Data = data
        };
        
        return await K8SClient.CoreV1.CreateNamespacedConfigMapAsync(configMap, destinationNamespace);
    }
    
    protected async Task<V1Secret?> CreateSecretAsync(
        string secretName,
        IDictionary<string, string> data,
        string destinationNamespace,
        ReflectorAnnotations reflectionAnnotations)
    {
        var secret = new V1Secret
        {
            ApiVersion = V1Secret.KubeApiVersion,
            Kind = V1Secret.KubeKind,
            Metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = destinationNamespace,
                Annotations = reflectionAnnotations.Build()
            },
            StringData = data,
            Type = "Opaque"
        };
        
        return await K8SClient.CoreV1.CreateNamespacedSecretAsync(secret, destinationNamespace);
    }
}