using Microsoft.EntityFrameworkCore.Storage;
using Rebus.Pipeline;
using Rebus.SqlServer.Outbox;
using Rebus.Transport;
using System.Data;
using System.Transactions;

namespace Microsoft.eShopOnContainers.Services.Catalog.API.IntegrationEvents;

public class CatalogIntegrationEventService : ICatalogIntegrationEventService
{
    private readonly IBus _bus;
    private readonly ILogger<CatalogIntegrationEventService> _logger;

    public CatalogIntegrationEventService(IBus bus,
        ILogger<CatalogIntegrationEventService> logger)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishEventsThroughEventBusAsync(IntegrationEvent @event, DbContext dbContext = null, Func<Task> action = null, bool insideHandler = true)
    {
        _logger.LogInformation("----- Publishing integration event:  {AppName} - ({@IntegrationEvent})", Program.AppName, @event);

        if (insideHandler)
        {
            var messageContext = MessageContext.Current
                    ?? throw new InvalidOperationException("Cannot get the message context outside of a Rebus handler");

            var transactionContext = messageContext.TransactionContext;

            var outboxConnection = transactionContext.Items.TryGetValue("current-outbox-connection", out var result)
                ? (OutboxConnection)result
                : throw new KeyNotFoundException("Could not find OutboxConnection under the key 'current-outbox-connection'");

            await _bus.Publish(@event);

            if (action is not null)
            {
                var executionStrategy = dbContext.Database.CreateExecutionStrategy();

                await executionStrategy.Execute(
                    async () =>
                    {
                        using var scope = new TransactionScope() ;

                        await action();

                        scope.Complete();

                    });
            }

            return;
        }

        if (dbContext is null)
        {
            await _bus.Publish(@event);
            return;
        }


        var connection = (Data.SqlClient.SqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }

        var transaction = (Data.SqlClient.SqlTransaction)dbContext.Database.CurrentTransaction?.GetDbTransaction();

        try
        {
            using var scope = new RebusTransactionScope();

            scope.UseOutbox(connection, transaction);

            await _bus.Publish(@event);

            if (action is not null)
            {
                await action();
            }

            await scope.CompleteAsync();

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Could not publish the integration event...", ex);
            throw;
        }
    }
}
