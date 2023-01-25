namespace Webhooks.API.IntegrationEvents;

public class ProductPriceChangedIntegrationEventHandler : IHandleMessages<ProductPriceChangedIntegrationEvent>
{
    public async Task Handle(ProductPriceChangedIntegrationEvent @event)
    {
        int i = 0;
    }
}
