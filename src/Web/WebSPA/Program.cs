using Amazon.CloudWatchLogs;

await BuildWebHost(args).RunAsync();

IWebHost BuildWebHost(string[] args) =>
    WebHost.CreateDefaultBuilder(args)
     .UseStartup<Startup>()
        .UseContentRoot(Directory.GetCurrentDirectory())
        .ConfigureAppConfiguration((builderContext, config) =>
        {
            config.AddEnvironmentVariables();
        })
        .ConfigureLogging((hostingContext, builder) =>
        {
            builder.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
            builder.AddConsole();
            builder.AddDebug();
        })
        .UseSerilog((builderContext, config) =>
        {
            var seqServerUrl = builderContext.Configuration["Serilog:SeqServerUrl"];
            var logstashUrl = builderContext.Configuration["Serilog:LogstashgUrl"];
            var lokiUrl = builderContext.Configuration["Serilog:LokiUrl"];
            var useAWS = bool.Parse(builderContext.Configuration["UseAWS"]);
            var useLocalStack = bool.Parse(builderContext.Configuration["LocalStack:UseLocalStack"]);
            var localStackUrl = builderContext.Configuration["LocalStack:LocalStackUrl"];

            config
             .MinimumLevel.Verbose()
             .Enrich.WithProperty("ApplicationContext", Program.AppName)
             .Enrich.FromLogContext()
             .Enrich.WithSpan()
             .WriteTo.Console()
             .ReadFrom.Configuration(builderContext.Configuration);

            if (!string.IsNullOrWhiteSpace(lokiUrl))
            {
                config.WriteTo.GrafanaLoki(lokiUrl);
            }

            if (!string.IsNullOrWhiteSpace(seqServerUrl))
            {
                config.WriteTo.Seq(seqServerUrl);
            }

            if (!string.IsNullOrWhiteSpace(logstashUrl))
            {
                config.WriteTo.Seq(logstashUrl);
            }

            if (useAWS)
            {
                var awsOptions = builderContext.Configuration.GetAWSOptions();
                AmazonCloudWatchLogsConfig awsConfig = new AmazonCloudWatchLogsConfig();
                awsConfig.RegionEndpoint = awsOptions.Region;

                if (useLocalStack)
                {
                    awsConfig.ServiceURL = localStackUrl;
                }

                var client = new AmazonCloudWatchLogsClient(awsConfig);

                config.WriteTo.AmazonCloudWatch(
                        // The name of the log group to log to
                        logGroup: "/eshop/webspa",
                        // A string that our log stream names should be prefixed with. We are just specifying the
                        // start timestamp as the log stream prefix
                        logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                        // The AWS CloudWatch client to use
                        cloudWatchClient: client);
            }
        })
        .Build();

public partial class Program
{
    public static readonly string AppName = "WebSPA";
}