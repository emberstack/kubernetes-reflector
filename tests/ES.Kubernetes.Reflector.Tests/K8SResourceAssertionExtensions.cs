using System.Diagnostics.CodeAnalysis;
using k8s;
using k8s.Models;
using Polly;
using Shouldly;

namespace ES.Kubernetes.Reflector.Tests;

public static class K8SResourceAssertionExtensions
{
    public static void ShouldBeCreated<T>([NotNull] this T? resource, string metadataName)
        where T : class, IKubernetesObject<V1ObjectMeta>
    {
        resource.ShouldNotBeNull();
        resource.Metadata.ShouldNotBeNull();
        resource.Metadata.Name.ShouldBe(metadataName);
    }

    public static void ShouldBeDeleted<T>([NotNull] this T resource, V1Status deletionStatus)
        where T : class, IKubernetesObject<V1ObjectMeta>
    {
        resource.ShouldNotBeNull();
        deletionStatus.ShouldNotBeNull();
        deletionStatus.Status.ShouldBe("Success");
    }


    public static async Task ShouldFindReplicatedResourceAsync<T>(this IKubernetes client,
        T resource,
        string namespaceName,
        ResiliencePipeline<bool> pipeline,
        CancellationToken cancellationToken = default)
        where T : class, IKubernetesObject<V1ObjectMeta>
    {
        var result =  await pipeline.ExecuteAsync(async token =>
        {
            IKubernetesObject<V1ObjectMeta>? retrievedResource = resource switch
            {
                V1ConfigMap => await client.CoreV1.ReadNamespacedConfigMapAsync(
                    resource.Metadata.Name, 
                    namespaceName,
                    cancellationToken: token),
                
                V1Secret => await client.CoreV1.ReadNamespacedSecretAsync(
                    resource.Metadata.Name,
                    namespaceName,
                    cancellationToken: token),
                
                _ => throw new NotSupportedException($"Resource type {typeof(T).Name} is not supported")
            };

            if (retrievedResource is null)
                return false;

            return (resource, retrievedResource) switch
            {
                (V1ConfigMap sourceConfigMap, V1ConfigMap replicatedConfigMap) => 
                    sourceConfigMap.Data.IsEqualTo(replicatedConfigMap.Data),
                
                (V1Secret sourceSecret, V1Secret replicatedSecret) => 
                    sourceSecret.Data.IsEqualTo(replicatedSecret.Data),
                
                _ => false
            };
        }, cancellationToken);
        
        result.ShouldBeTrue();
    }
    
    private static bool IsEqualTo<TKey, TValue>(this IDictionary<TKey, TValue>? dict1, IDictionary<TKey, TValue>? dict2)
        where TKey : notnull
    {
        if (dict1 == null && dict2 == null)
            return true;

        if (dict1 == null || dict2 == null)
            return false;

        if (dict1.Count != dict2.Count)
            return false;

        return dict1.All(kvp => dict2.TryGetValue(kvp.Key, out var value) && 
                                AreValuesEqual(kvp.Value, value));
    }

    private static bool AreValuesEqual<TValue>(TValue value1, TValue value2)
    {
        if (value1 is byte[] bytes1 && value2 is byte[] bytes2)
            return bytes1.SequenceEqual(bytes2);
    
        return EqualityComparer<TValue>.Default.Equals(value1, value2);
    }
}