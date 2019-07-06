using System;
using System.IO;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ES.Kubernetes.Reflector
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddCommandLine(args ?? new string[0])
                    .AddEnvironmentVariables("ES_")
                    .AddJsonFile("reflector.logging.json")
                    .Build())
                .CreateLogger();

            try
            {
                Log.Information("Host starting");
                await CreateWebHostBuilder(args).Build().RunAsync();
                Log.Information("Host stopped");
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

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            return WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseEnvironment(Environment.GetEnvironmentVariable($"ES_{nameof(Environment)}") ??
                                EnvironmentName.Production)
                .SuppressStatusMessages(true)
                .ConfigureAppConfiguration((ctx, builder) =>
                {
                    builder.SetBasePath(Directory.GetCurrentDirectory());
                    builder.AddCommandLine(args ?? new string[0]);
                    builder.AddEnvironmentVariables("ES_");
                    builder.AddJsonFile("reflector.appsettings.json", false, true);
                })
                .ConfigureServices(services =>
                {
                    services.AddAutofac();
                    services.AddHttpClient();
                    services.AddOptions();
                })
                .UseSerilog();
        }
    }
}