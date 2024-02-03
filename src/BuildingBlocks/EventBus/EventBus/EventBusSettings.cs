namespace Microsoft.eShopOnContainers.BuildingBlocks.EventBus;

public class EventBusSettings
{
    public string Host { get; set; }

    public string UserName { get; set; }

    public string Password { get; set; }

    public bool IsSecure { get; set; }

    public bool PersistenceEnabled { get; set; }

    public bool OutboxEnabled { get; set; }

    public string EndpointName { get; set; }

    public bool RabbitMQEnabled { get; set; }

    public int RetryCount { get; set; }

    public bool AuditEnabled { get; set; }

    public FleetManager FleetManager { get; set; }
}

public class FleetManager
{
    public bool Enabled { get; set; }

    public string Url { get; set; }

    public string ApiKey { get; set; }
}
