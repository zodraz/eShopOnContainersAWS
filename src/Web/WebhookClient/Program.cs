CreateWebHostBuilder(args).Build().Run();


IWebHostBuilder CreateWebHostBuilder(string[] args) =>
    WebHost.CreateDefaultBuilder(args)
        .UseStartup<Startup>();


public partial class Program
{
    private static readonly string _namespace = typeof(Startup).Namespace;
    public static readonly string AppName = _namespace;
}