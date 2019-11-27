using Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SiloHost
{
    public class Program
    {
        public static IConfiguration Configuration { get; set; }
        public static int Main(string[] args)
        {
            return RunMainAsync().Result;
        }
        private static async Task<int> RunMainAsync()
        {
            try
            {
                var environmentName = GetEnvironment();
                var configurationBuilder = CreateConfigurationBuilder(environmentName);
                Configuration = configurationBuilder.Build();

                var host = StartSilo();
                await host.RunAsync();

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }
        private static IHost StartSilo()
        {
            var invariant = Configuration.GetSection("Invariant").GetValue<string>("DefaultDatabase");
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            var name = Dns.GetHostName(); // get container id
            foreach (var address in Dns.GetHostEntry(name).AddressList) {
                Console.WriteLine(address.ToString());
            }
            var host = new HostBuilder()
                .UseOrleans((context, siloBuilder) =>
                {

                    siloBuilder.Configure<ProcessExitHandlingOptions>(options =>
                    {
                        // https://github.com/dotnet/orleans/issues/5552#issuecomment-486938815
                        options.FastKillOnProcessExit = false;
                    })
                    .Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = Constants.ClusterId;
                        options.ServiceId = Constants.ServiceId;
                    })
                    .UseAdoNetClustering(options =>
                    {
                        options.Invariant = invariant;
                        options.ConnectionString = connectionString;
                    })
                    .AddAdoNetGrainStorage(Constants.OrleansDataStorageProvider, options =>
                    {
                        options.Invariant = invariant;
                        options.ConnectionString = connectionString;
                        options.UseJsonFormat = true;
                    })
                    .Configure<EndpointOptions>(options =>
                    {
                        // Port to use for Silo-to-Silo
                        options.SiloPort = Constants.SiloPort;
                        // Port to use for the gateway
                        options.GatewayPort = Constants.GatewayPort;
                        // IP Address to advertise in the cluster
                        options.AdvertisedIPAddress = IPAddress.Parse("20.184.59.135");
                        // The socket used for silo-to-silo will bind to this endpoint
                        options.GatewayListeningEndpoint = new IPEndPoint(IPAddress.Any, 40000);
                        // The socket used by the gateway will bind to this endpoint
                        options.SiloListeningEndpoint = new IPEndPoint(IPAddress.Any, 50000);

                    })
                    // need to configure a grain storage called "PubSubStore" for using
                    // streaming with ExplicitSubscribe pubsub type. Depends on your
                    // application requirements, you can configure your silo with other
                    // stream providers, which can provide other features, such as
                    // persistence or recoverability. For more information, please see
                    // http://dotnet.github.io/orleans/Documentation/streaming/streams_programming_APIs.html#fully-managed-and-reliable-streaming-pub-suba-namefully-managed-and-reliable-streaming-pub-suba
                    .AddMemoryGrainStorage(Constants.OrleansMemoryProvider)
                    .AddSimpleMessageStreamProvider(Constants.OrleansStreamProvider);
                })
                .ConfigureLogging(logging => logging.AddConsole())
                .Build();

            return host;
        }
        private static string GetEnvironment()
        {
            var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.IsNullOrEmpty(environmentName))
            {
                Console.WriteLine("Production");
                return "Production";
            }
            Console.WriteLine(environmentName);
            return environmentName;
        }
        private static IConfigurationBuilder CreateConfigurationBuilder(string environmentName)
        {
            var config = new ConfigurationBuilder()
                             .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                             .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
                             .AddEnvironmentVariables();
            return config;
        }
    }
}
