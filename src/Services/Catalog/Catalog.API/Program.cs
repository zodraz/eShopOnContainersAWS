var configuration = GetConfiguration();

Log.Logger = CreateSerilogLogger(configuration);

try
{
    Log.Information("Configuring web host ({ApplicationContext})...", Program.AppName);
    var host = CreateHostBuilder(configuration, args);

    Log.Information("Applying migrations ({ApplicationContext})...", Program.AppName);
    host.MigrateDbContext<CatalogContext>((context, services) =>
    {
        var env = services.GetService<IWebHostEnvironment>();
        var settings = services.GetService<IOptions<CatalogSettings>>();
        var logger = services.GetService<ILogger<CatalogContextSeed>>();

        Log.Information("bout to enter...");
        new CatalogContextSeed().SeedAsync(context, env, settings, logger).Wait();
    });

    Log.Information("Starting web host ({ApplicationContext})...", Program.AppName);
    host.Run();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Program terminated unexpectedly ({ApplicationContext})!", Program.AppName);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

IWebHost CreateHostBuilder(IConfiguration configuration, string[] args) =>
  WebHost.CreateDefaultBuilder(args)
      .AddAwsSecrets(configuration)
      .CaptureStartupErrors(false)
      .ConfigureKestrel(options =>
      {
          var ports = GetDefinedPorts(configuration);
          options.Listen(IPAddress.Any, ports.httpPort, listenOptions =>
          {
              listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
          });
          options.Listen(IPAddress.Any, ports.grpcPort, listenOptions =>
          {
              listenOptions.Protocols = HttpProtocols.Http2;
          });

      })
      .UseStartup<Startup>()
      .UseContentRoot(Directory.GetCurrentDirectory())
      .UseWebRoot("Pics")
      .UseSerilog()
      .Build();

Serilog.ILogger CreateSerilogLogger(IConfiguration configuration)
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
                logGroup: "/eshop/catalog",
                // A string that our log stream names should be prefixed with. We are just specifying the
                // start timestamp as the log stream prefix
                logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                // The AWS CloudWatch client to use
                cloudWatchClient: client);
    }

    return cfg.CreateLogger();
}

(int httpPort, int grpcPort) GetDefinedPorts(IConfiguration config)
{
    var grpcPort = config.GetValue("GRPC_PORT", 5001);
    var port = config.GetValue("PORT", 80);
    return (port, grpcPort);
}

IConfiguration GetConfiguration()
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    return builder.Build();
}

public partial class Program
{
    public static string Namespace = typeof(Startup).Namespace;
    public static string AppName = Namespace.Substring(Namespace.LastIndexOf('.', Namespace.LastIndexOf('.') - 1) + 1);
}