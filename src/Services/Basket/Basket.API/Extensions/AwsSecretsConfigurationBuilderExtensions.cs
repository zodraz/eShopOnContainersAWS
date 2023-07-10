namespace Basket.API.Extensions;

public static class AwsSecretsConfigurationBuilderExtensions
{
    public class VaultSettings
    {
        /// <summary>
        /// The allowed secret groups, e.g. Shared or MyAppsSecrets
        /// </summary>
        public List<string> SecretGroups { get; set; }
    }

    public static IWebHostBuilder AddAwsSecrets(this IWebHostBuilder hostBuilder, IConfiguration config)
    {
        return hostBuilder.ConfigureAppConfiguration((hostingContext, configBuilder) =>
        {
            if (config.GetValue("UseVault", true) && config.GetValue("UseAWS", true))
            {
                // Call our extension method
                configBuilder.AddAwsSecrets(hostingContext);
            }
        });
    }

    public static IConfigurationBuilder AddAwsSecrets(this IConfigurationBuilder builder,
        WebHostBuilderContext hostingContext)
    {
        IConfiguration partialConfig = builder.Build();

        var settings = new VaultSettings();
        partialConfig.GetSection("Vault").Bind(settings);

        var awsOptions = partialConfig.GetAWSOptions();
        var useLocalStack = bool.Parse(partialConfig["LocalStack:UseLocalStack"]);
        var localStackUrl = partialConfig["LocalStack:LocalStackUrl"];

        var env = hostingContext.HostingEnvironment.EnvironmentName;
        var allowedPrefixes = settings.SecretGroups.Select(grp => $"{env}/{grp}").ToList();

        return builder.AddSecretsManager(configurator: opts =>
        {
            opts.SecretFilter = entry => HasPrefix(allowedPrefixes, entry);
            opts.KeyGenerator = (entry, key) => GenerateKey(allowedPrefixes, key);

            if (useLocalStack)
            {
                opts.ConfigureSecretsManagerConfig = c => {
                    c.ServiceURL = localStackUrl; // The url that's used by localstack
                };
                //https://github.com/Kralizek/AWSSecretsManagerConfigurationExtensions/issues/20
                opts.ConfigureSecretsManagerConfig = c => { c.AuthenticationRegion = "eu-central-1"; };
            } 
        }, region: awsOptions.Region);
    }

    // Only load entries that start with any of the allowed prefixes
    private static bool HasPrefix(List<string> allowedPrefixes, SecretListEntry entry)
    {
        return allowedPrefixes.Any(prefix => entry.Name.StartsWith(prefix));
    }

    // Strip the prefix and replace '__' with ':'
    private static string GenerateKey(IEnumerable<string> prefixes, string secretValue)
    {
        foreach (var prefix in prefixes)
        {
            //In the case we have JSON value let's add a colon to search for match
            var prefixWitcolon = prefix + ":";

            if (secretValue.StartsWith(prefixWitcolon))
            {
                // Strip the prefix, and replace "__" with ":"
                return secretValue.Substring(prefixWitcolon.Length).Replace("__", ":");
            }

            if (secretValue.StartsWith(prefix))
            {
                // Strip the prefix, and replace "__" with ":"
                return secretValue.Substring(prefix.Length).Replace("__", ":");
            }
        }

        return secretValue;
    }
}