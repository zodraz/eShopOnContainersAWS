using Amazon.CloudWatchLogs;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Sinks.AwsCloudWatch;
using Serilog.Sinks.Grafana.Loki;
using System;
using System.IO;

namespace Microsoft.eShopOnContainers.Services.Locations.API
{
    public class Program
    {
        public static readonly string Namespace = typeof(Program).Namespace;
        public static readonly string AppName = Namespace.Substring(Namespace.LastIndexOf('.', Namespace.LastIndexOf('.') - 1) + 1);

        public static int Main(string[] args)
        {
            var configuration = GetConfiguration();

            Log.Logger = CreateSerilogLogger(configuration);

            try
            {
                Log.Information("Configuring web host ({ApplicationContext})...", AppName);
                var host = BuildWebHost(configuration, args);

                Log.Information("Starting web host ({ApplicationContext})...", AppName);
                host.Run();

                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Program terminated unexpectedly ({ApplicationContext})!", AppName);
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static IWebHost BuildWebHost(IConfiguration configuration, string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .AddAwsSecrets(configuration)
                .CaptureStartupErrors(false)
                .ConfigureAppConfiguration(x => x.AddConfiguration(configuration))
                .UseStartup<Startup>()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseSerilog()
                .Build();

        private static Serilog.ILogger CreateSerilogLogger(IConfiguration configuration)
        {
            var seqServerUrl = configuration["Serilog:SeqServerUrl"];
            var logstashUrl = configuration["Serilog:LogstashgUrl"];
            var lokiUrl = configuration["Serilog:LokiUrl"];
            var useAWS = bool.Parse(configuration["UseAWS"]);
            var useLocalStack = bool.Parse(configuration["LocalStack:UseLocalStack"]);
            var localStackUrl = configuration["LocalStack:LocalStackUrl"];

            var cfg = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.WithProperty("ApplicationContext", Program.AppName)
                    .Enrich.FromLogContext()
                    .Enrich.WithSpan()
                    .WriteTo.Console()
                    .ReadFrom.Configuration(configuration);

            if (!string.IsNullOrWhiteSpace(lokiUrl))
            {
                cfg.WriteTo.GrafanaLoki(lokiUrl);
            }

            if (!string.IsNullOrWhiteSpace(seqServerUrl))
            {
                cfg.WriteTo.Seq(seqServerUrl);
            }

            if (!string.IsNullOrWhiteSpace(logstashUrl))
            {
                cfg.WriteTo.Seq(logstashUrl);
            }

            if (useAWS)
            {
                var awsOptions = configuration.GetAWSOptions();
                AmazonCloudWatchLogsConfig awsConfig = new AmazonCloudWatchLogsConfig();
                awsConfig.RegionEndpoint = awsOptions.Region;

                if (useLocalStack)
                {
                    awsConfig.ServiceURL = localStackUrl;
                }

                var client = new AmazonCloudWatchLogsClient(awsConfig);

                cfg.WriteTo.AmazonCloudWatch(
                        // The name of the log group to log to
                        logGroup: "/eshop/locations",
                        // A string that our log stream names should be prefixed with. We are just specifying the
                        // start timestamp as the log stream prefix
                        logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                        // The AWS CloudWatch client to use
                        cloudWatchClient: client);
            }

            return cfg.CreateLogger();

        }

        private static IConfiguration GetConfiguration()
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            return builder.Build();
        }
    }
}