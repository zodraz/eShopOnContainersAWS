using Amazon.Extensions.NETCore.Setup;

namespace Microsoft.eShopOnContainers.Services.Payment.API;

public class PaymentSettings
{
    public bool PaymentSucceeded { get; set; }
    public string ConnectionString { get; set; }
    public bool UseAWS { get; set; }
    public bool UseVault { get; set; }
    public AWSOptions AWSOptions { get; set; }
    public LocalStack LocalStack { get; set; }
    public EventBusSettings EventBus { get; set; }
    public string OtlpEndpoint { get; set; }
    public VaultSettings Vault { get; set; }
}

public class LocalStack
{
    public bool UseLocalStack { get; set; }
    public string LocalStackUrl { get; set; }
}

public class VaultSettings
{
    /// <summary>
    /// The allowed secret groups, e.g. Shared or MyAppsSecrets
    /// </summary>
    public List<string> SecretGroups { get; set; }
}