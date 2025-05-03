using System.Net;
using ES.Kubernetes.Reflector.Tests.Integration.Fixtures;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Polly;
using Polly.Retry;

namespace ES.Kubernetes.Reflector.Tests.Integration.Base;

public class BaseIntegrationTest(ReflectorIntegrationFixture integrationFixture)
{
    protected static readonly ResiliencePipeline<bool> ResourceExistsResiliencePipeline =
        new ResiliencePipelineBuilder<bool>()
            .AddRetry(new RetryStrategyOptions<bool>
            {
                ShouldHandle = new PredicateBuilder<bool>()
                    .Handle<HttpOperationException>(ex =>
                        ex.Response.StatusCode == HttpStatusCode.NotFound)
                    .HandleResult(false),
                MaxRetryAttempts = 10,
                Delay = TimeSpan.FromSeconds(1)
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();

    protected async Task<IKubernetes> GetKubernetesClient() =>
        await integrationFixture.Kubernetes.GetKubernetesClient();

    protected async Task<V1Namespace?> CreateNamespaceAsync(string name)
    {
        var client = await GetKubernetesClient();
        var ns = new V1Namespace
        {
            ApiVersion = V1Namespace.KubeApiVersion,
            Kind = V1Namespace.KubeKind,
            Metadata = new V1ObjectMeta
            {
                Name = name
            }
        };

        return await client.CoreV1.CreateNamespaceAsync(ns);
    }


    protected async Task DelayForReflection() =>
        await Task.Delay(TimeSpan.FromSeconds(1));
}