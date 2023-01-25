namespace Microsoft.eShopOnContainers.Services.IntegrationEvents.Events;

public record OrderStatusChangedToStockConfirmedIntegrationEvent : IntegrationEvent
{
    public int OrderId { get; }

    public OrderStatusChangedToStockConfirmedIntegrationEvent(int orderId)
        => OrderId = orderId;
}
