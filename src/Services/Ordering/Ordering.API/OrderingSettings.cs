using Amazon.Extensions.NETCore.Setup;

namespace Microsoft.eShopOnContainers.Services.Ordering.API;

public class OrderingSettings
{
    public bool UseCustomizationData { get; set; }

    public string ConnectionString { get; set; }

    public int GracePeriodTime { get; set; }

    public int CheckUpdateTime { get; set; }
    public bool UseAWS { get; set; }
    public bool UseVault { get; set; }
    public AWSOptions AWSOptions { get; set; }
    public LocalStack LocalStack { get; set; }
    public EventBusSettings EventBus { get; set; }
}

public class LocalStack
{
    public bool UseLocalStack { get; set; }
    public string LocalStackUrl { get; set; }
}
