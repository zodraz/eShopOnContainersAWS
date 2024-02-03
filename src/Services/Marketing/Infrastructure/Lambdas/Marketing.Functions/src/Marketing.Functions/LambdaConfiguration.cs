using Microsoft.Extensions.Configuration;

namespace Marketing.Functions;

public interface ILambdaConfiguration
{
    IConfiguration Configuration { get; }
}

public class LambdaConfiguration : ILambdaConfiguration
{
    public IConfiguration Configuration => new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .Build();
}
