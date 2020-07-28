using System;
using System.IO;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using ES.Kubernetes.Reflector.Core.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ES.Kubernetes.Reflector.Host
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("reflector.logging.json")
                    .AddEnvironmentVariables("ES_")
                    .AddCommandLine(args ?? new string[0])
                    .Build())
                .Destructure.ByTransforming<KubernetesObjectId>(s => s.ToString())
                .CreateLogger().ForContext<Program>();


            try
            {
                Log.Information("Starting host");
                await CreateHostBuilder(args).Build().RunAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseEnvironment(Environment.GetEnvironmentVariable($"ES_{nameof(Environment)}") ??
                                Environments.Production)
                //Add configuration
                .ConfigureAppConfiguration((ctx, config) =>
                {
                    config.AddJsonFile("reflector.appsettings.json", false, true);
                    config.AddJsonFile("reflector.logging.json", false, true);
                })
                //Use Serilog
                .UseSerilog()
                //Suppress startup status messages
                .ConfigureServices((ctx, services) =>
                    services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = false))
                //Configure dependency injection
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseStartup<Startup>()
                        .UseUrls("http://*:25080");
                });
        }
    }
}