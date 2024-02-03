namespace Microsoft.eShopOnContainers.Services.Catalog.API.IntegrationEvents;

public interface ICatalogIntegrationEventService
{
    Task PublishEventsThroughEventBusAsync(IntegrationEvent @event, DbContext dbContext = null, Func<Task> action = null, bool insideHandler = true);
}
