string Namespace = typeof(Startup).Namespace;
string AppName = Namespace.Substring(Namespace.LastIndexOf('.', Namespace.LastIndexOf('.') - 1) + 1);

var configuration = GetConfiguration();

Log.Logger = CreateSerilogLogger(configuration);

try
{
    Log.Information("Configuring web host ({ApplicationContext})...", AppName);
    var host = BuildWebHost(configuration, args);

    Log.Information("Applying migrations ({ApplicationContext})...", AppName);
    host.MigrateDbContext<PersistedGrantDbContext>((_, __) => { })
        .MigrateDbContext<ApplicationDbContext>((context, services) =>
        {
            var env = services.GetService<IWebHostEnvironment>();
            var logger = services.GetService<ILogger<ApplicationDbContextSeed>>();
            var settings = services.GetService<IOptions<AppSettings>>();

            new ApplicationDbContextSeed()
                .SeedAsync(context, env, logger, settings)
                .Wait();
        })
        .MigrateDbContext<ConfigurationDbContext>((context, services) =>
        {
            new ConfigurationDbContextSeed()
                .SeedAsync(context, configuration)
                .Wait();
        });

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

IWebHost BuildWebHost(IConfiguration configuration, string[] args) =>
    WebHost.CreateDefaultBuilder(args)
        .AddAwsSecrets(configuration)
        .CaptureStartupErrors(false)
        .ConfigureAppConfiguration(x => x.AddConfiguration(configuration))
        .UseStartup<Startup>()
        .UseContentRoot(Directory.GetCurrentDirectory())
        .UseSerilog()
        .Build();

Serilog.ILogger CreateSerilogLogger(IConfiguration configuration)
{
    var seqServerUrl = configuration["Serilog:SeqServerUrl"];
    var logstashUrl = configuration["Serilog:LogstashgUrl"];
    var useAWS = bool.Parse(configuration["UseAWS"]);
    var lokiUrl = configuration["Serilog:LokiUrl"];
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
                logGroup: "/eshop/identity",
                // A string that our log stream names should be prefixed with. We are just specifying the
                // start timestamp as the log stream prefix
                logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                // The AWS CloudWatch client to use
                cloudWatchClient: client);
    }

    return cfg.CreateLogger();
}

IConfiguration GetConfiguration()
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    var config = builder.Build();

    return builder.Build();
}

public partial class Program
{

    public static string Namespace = typeof(Startup).Namespace;
    public static string AppName = Namespace.Substring(Namespace.LastIndexOf('.', Namespace.LastIndexOf('.') - 1) + 1);
}