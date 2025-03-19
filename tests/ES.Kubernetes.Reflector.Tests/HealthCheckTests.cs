using System.Net;
using Shouldly;

namespace ES.Kubernetes.Reflector.Tests;

public class HealthCheckTests(CustomWebApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task LivenessHealthCheck_Should_Return_Healthy()
    {
        var response = await Client.GetAsync(IgniteSettings.HealthChecks.LivenessEndpointPath);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/plain");
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldBe("Healthy");
    }
    
    [Fact]
    public async Task ReadinessHealthCheck_Should_Be_Unavailable()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        var response = await Client.GetAsync(IgniteSettings.HealthChecks.ReadinessEndpointPath);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}