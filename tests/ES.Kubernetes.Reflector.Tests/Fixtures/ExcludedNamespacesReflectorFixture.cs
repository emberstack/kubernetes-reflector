using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ES.Kubernetes.Reflector.Tests.Fixtures;

public sealed class ExcludedNamespacesReflectorFixture : ReflectorFixture
{
    public const string ExcludedPattern = "excluded-*";

    protected override void ConfigureAdditionalWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reflector:Watcher:ExcludedNamespaces"] = ExcludedPattern
            });
        });
    }
}
