namespace Microsoft.eShopOnContainers.Services.Ordering.API.Application.IntegrationEvents;

public interface IOrderingIntegrationEventService
{
    Task PublishEventsThroughEventBusAsync(IntegrationEvent @event);
}