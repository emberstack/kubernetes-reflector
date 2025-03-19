using k8s;
using Xunit.Abstractions;

namespace ES.Kubernetes.Reflector.Tests;

public class SecretMirrorTests(CustomWebApplicationFactory factory, ITestOutputHelper testOutputHelper)
    : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Create_secret_With_ReflectionEnabled_Should_Replicated_To_Allowed_Namespaces()
    {
        // Arrange
        const string sourceNamespace = "dev002";
        const string destinationNamespace = "qa002";
        string sourceSecret = $"test-secret-{Guid.NewGuid()}";
        var secretData = new Dictionary<string, string>
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
        var createdSecret = await CreateSecretAsync(
            sourceSecret,
            secretData,
            sourceNamespace,
            reflectorAnnotations);
        createdSecret.ShouldBeCreated(sourceSecret);
        testOutputHelper.WriteLine($"Secret {sourceSecret} created in {sourceNamespace} namespace");

        // Assert
        await K8SClient.ShouldFindReplicatedResourceAsync(createdSecret, destinationNamespace, Pipeline);
        testOutputHelper.WriteLine($"Secret {sourceSecret} found in {destinationNamespace} namespace");
    }
    
    [Fact]
    public async Task Create_secret_With_DefaultReflectorAnnotations_Should_Replicated_To_All_Namespaces()
    {
        // Arrange
        const string sourceNamespace = "dev003";
        string sourceSecret = $"test-secret-{Guid.NewGuid()}";
        var secretData = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };
        var reflectorAnnotations = new ReflectorAnnotations();

        var createdSourceNs = await CreateNamespaceAsync(sourceNamespace);
        createdSourceNs.ShouldBeCreated(sourceNamespace);
        testOutputHelper.WriteLine($"Namespace {sourceNamespace} created");
        
        // Act
        var createdSecret = await CreateSecretAsync(
            sourceSecret,
            secretData,
            sourceNamespace,
            reflectorAnnotations);
        createdSecret.ShouldBeCreated(sourceSecret);
        testOutputHelper.WriteLine($"Secret {sourceSecret} created in {sourceNamespace} namespace");
        
        // Assert
        var namespaces = await K8SClient.CoreV1.ListNamespaceAsync();
        var targetNamespaces = namespaces.Items
            .Where(ns => !string.Equals(ns.Metadata.Name, sourceNamespace, StringComparison.Ordinal))
            .ToList();
        await Task.WhenAll(targetNamespaces.Select(async ns =>
        {
            await K8SClient.ShouldFindReplicatedResourceAsync(createdSecret, ns.Metadata.Name, Pipeline);
            testOutputHelper.WriteLine($"Secret {sourceSecret} found in {ns.Metadata.Name} namespace");
        }));
    }
    
} 