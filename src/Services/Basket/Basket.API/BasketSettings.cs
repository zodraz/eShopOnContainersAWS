namespace Microsoft.eShopOnContainers.Services.Basket.API;

public class BasketSettings
{
    public string CacheConnectionString { get; set; }

    public EventBusSettings EventBus { get; set; }
}