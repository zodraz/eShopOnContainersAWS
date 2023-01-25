using Microsoft.EntityFrameworkCore.Storage;
using Rebus.Pipeline;
using Rebus.SqlServer.Outbox;
using Rebus.Transport;
using System.Data;
using System.Transactions;

namespace Microsoft.eShopOnContainers.Services.Ordering.API.Application.IntegrationEvents;

public class OrderingIntegrationEventService : IOrderingIntegrationEventService
{
    private readonly IBus _bus;
    private readonly ILogger<OrderingIntegrationEventService> _logger;

    public OrderingIntegrationEventService(IBus bus,
        ILogger<OrderingIntegrationEventService> logger)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishEventsThroughEventBusAsync(IntegrationEvent @event)
    {
        _logger.LogInformation("----- Publishing integration event:  {AppName} - ({@IntegrationEvent})", Program.AppName, @event);

        await _bus.Publish(@event);
    }
}