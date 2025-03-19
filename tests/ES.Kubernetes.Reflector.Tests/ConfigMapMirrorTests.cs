using k8s;
using Xunit.Abstractions;

namespace ES.Kubernetes.Reflector.Tests;

public class ConfigMapMirrorTests(CustomWebApplicationFactory factory, ITestOutputHelper testOutputHelper)
    : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Create_configMap_With_ReflectionEnabled_Should_Replicated_To_Allowed_Namespaces()
    {
        // Arrange
        const string sourceNamespace = "dev1";
        const string destinationNamespace = "qa";
        string sourceConfigMap = $"test-configmap-{Guid.NewGuid()}";
        var configMapData = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };
        var reflectorAnnotations = new ReflectorAnnotations()
            .WithReflectionAllowed(true)
            .WithAllowedNamespaces(destinationNamespace)
            .WithAutoEnabled(true);

        var createdSourceNs = await CreateNamespaceAsync(sourceNamespace);
        createdSourceNs.ShouldBeCreated(sourceNamespace);
        testOutputHelper.WriteLine($"Namespace {sourceNamespace} created");

        // Act
        var createdDestinationNs = await CreateNamespaceAsync(destinationNamespace);
        createdDestinationNs.ShouldBeCreated(destinationNamespace);
        testOutputHelper.WriteLine($"Namespace {destinationNamespace} created");
        var createdConfigMap = await CreateConfigMapAsync(
            sourceConfigMap,
            configMapData,
            sourceNamespace,
            reflectorAnnotations);
        createdConfigMap.ShouldBeCreated(sourceConfigMap);
        testOutputHelper.WriteLine($"ConfigMap {sourceConfigMap} created in {sourceNamespace} namespace");

        // Assert
        await K8SClient.ShouldFindReplicatedResourceAsync(createdConfigMap, destinationNamespace, Pipeline);
        testOutputHelper.WriteLine($"ConfigMap {sourceConfigMap} found in {destinationNamespace} namespace");
    }

    [Fact]
    public async Task Create_configMap_With_DefaultReflectorAnnotations_Should_Replicated_To_All_Namespaces()
    {
        // Arrange
        const string sourceNamespace = "dev2";
        string sourceConfigMap = $"test-configmap-{Guid.NewGuid()}";
        var configMapData = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };
        var reflectorAnnotations = new ReflectorAnnotations();

        var createdSourceNs = await CreateNamespaceAsync(sourceNamespace);
        createdSourceNs.ShouldBeCreated(sourceNamespace);
        testOutputHelper.WriteLine($"Namespace {sourceNamespace} created");

        // Act
        var createdConfigMap = await CreateConfigMapAsync(
            sourceConfigMap,
            configMapData,
            sourceNamespace,
            reflectorAnnotations);
        createdConfigMap.ShouldBeCreated(sourceConfigMap);
        testOutputHelper.WriteLine($"ConfigMap {sourceConfigMap} created in {sourceNamespace} namespace");

        // Assert
        var namespaces = await K8SClient.CoreV1.ListNamespaceAsync();
        var targetNamespaces = namespaces.Items
            .Where(ns => !string.Equals(ns.Metadata.Name, sourceNamespace, StringComparison.Ordinal))
            .ToList();

        await Task.WhenAll(targetNamespaces.Select(async ns =>
        {
            await K8SClient.ShouldFindReplicatedResourceAsync(createdConfigMap, ns.Metadata.Name, Pipeline);
            testOutputHelper.WriteLine($"ConfigMap {sourceConfigMap} found in {ns.Metadata.Name} namespace");
        }));
    }
}