var configuration = GetConfiguration();

Log.Logger = CreateSerilogLogger(configuration);

try
{
    Log.Information("Configuring web host ({ApplicationContext})...", Program.AppName);
    var host = BuildWebHost(configuration, args);

    LogPackagesVersionInfo();

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
                logGroup: "/eshop/webstatus",
                // A string that our log stream names should be prefixed with. We are just specifying the
                // start timestamp as the log stream prefix
                logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                // The AWS CloudWatch client to use
                cloudWatchClient: client);
    }

    return cfg.CreateLogger();
}

static IConfiguration GetConfiguration()
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    return builder.Build();
}

string GetVersion(Assembly assembly)
{
    try
    {
        return $"{assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version} ({assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split()[0]})";
    }
    catch
    {
        return string.Empty;
    }
}

void LogPackagesVersionInfo()
{
    var assemblies = new List<Assembly>();

    foreach (var dependencyName in typeof(Program).Assembly.GetReferencedAssemblies())
    {
        try
        {
            // Try to load the referenced assembly...
            assemblies.Add(Assembly.Load(dependencyName));
        }
        catch
        {
            // Failed to load assembly. Skip it.
        }
    }

    var versionList = assemblies.Select(a => $"-{a.GetName().Name} - {GetVersion(a)}").OrderBy(value => value);

    Log.Logger.ForContext("PackageVersions", string.Join("\n", versionList)).Information("Package versions ({ApplicationContext})", Program.AppName);
}

public partial class Program
{
    private static readonly string _namespace = typeof(Startup).Namespace;
    public static readonly string AppName = _namespace;
}