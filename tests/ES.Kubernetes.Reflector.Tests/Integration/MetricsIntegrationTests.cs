using System.Net;
using ES.Kubernetes.Reflector.Tests.Integration.Base;
using ES.Kubernetes.Reflector.Tests.Integration.Fixtures;
using JetBrains.Annotations;

namespace ES.Kubernetes.Reflector.Tests.Integration;

[PublicAPI]
public class MetricsIntegrationTests(ReflectorIntegrationFixture integrationFixture)
    : BaseIntegrationTest(integrationFixture)
{
    private readonly ReflectorIntegrationFixture _integrationFixture = integrationFixture;

    [Fact]
    public async Task MetricsEndpoint_Should_Return_Prometheus_Content()
    {
        var httpClient = _integrationFixture.Reflector.CreateClient();

        var response = await httpClient.GetAsync("/metrics", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("target_info", content);
    }
}
