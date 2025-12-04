using System.Net;
using ES.FX.Ignite.Configuration;
using ES.Kubernetes.Reflector.Tests.Integration.Base;
using ES.Kubernetes.Reflector.Tests.Integration.Fixtures;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(ReflectorIntegrationFixture))]

namespace ES.Kubernetes.Reflector.Tests.Integration;

[PublicAPI]
public class HealthCheckIntegrationTests(ReflectorIntegrationFixture integrationFixture)
    : BaseIntegrationTest(integrationFixture)
{
    private readonly ReflectorIntegrationFixture _integrationFixture = integrationFixture;

    [Fact]
    public async Task LivenessHealthCheck_Should_Return_Healthy()
    {
        var httpClient = _integrationFixture.Reflector.CreateClient();
        var settings = _integrationFixture.Reflector.Services.GetRequiredService<IgniteSettings>();

        var response = await httpClient.GetAsync(settings.AspNetCore.HealthChecks.LivenessEndpointPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessHealthCheck_Should_Return_Healthy()
    {
        var settings = _integrationFixture.Reflector.Services.GetRequiredService<IgniteSettings>();
        var httpClient = _integrationFixture.Reflector.CreateClient();

        var response = await httpClient.GetAsync(settings.AspNetCore.HealthChecks.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}