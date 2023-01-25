using Microsoft.EntityFrameworkCore.Storage;
using Rebus.Config.Outbox;
using Rebus.Transport;
using System.Data;

namespace Microsoft.eShopOnContainers.Services.Ordering.API.Application.Behaviors;

public class TransactionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TransactionBehaviour<TRequest, TResponse>> _logger;
    private readonly OrderingContext _dbContext;
    //private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;

    public TransactionBehaviour(OrderingContext dbContext,
        //IOrderingIntegrationEventService orderingIntegrationEventService,
        ILogger<TransactionBehaviour<TRequest, TResponse>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentException(nameof(OrderingContext));
        //_orderingIntegrationEventService = orderingIntegrationEventService ?? throw new ArgumentException(nameof(orderingIntegrationEventService));
        _logger = logger ?? throw new ArgumentException(nameof(Microsoft.Extensions.Logging.ILogger));
    }

    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next)
    {
        var response = default(TResponse);
        var typeName = request.GetGenericTypeName();

        try
        {
            if (_dbContext.HasActiveTransaction)
            {
                return await next();
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                var connection = (Data.SqlClient.SqlConnection)_dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    _dbContext.Database.OpenConnection();
                }

                var transactionCreated = await _dbContext.BeginTransactionAsync();

                var transaction = (Data.SqlClient.SqlTransaction)transactionCreated.GetDbTransaction();

                using (LogContext.PushProperty("TransactionContext", transaction.ToString()))
                {
                    _logger.LogInformation("----- Begin transaction {TransactionId} for {CommandName} ({@Command})", transaction.ToString(), typeName, request);
                    try
                    {
                        using var scope = new RebusTransactionScope();

                        scope.UseOutbox(connection, transaction);

                        response = await next();

                        _logger.LogInformation("----- Commit transaction {TransactionId} for {CommandName}", transaction.ToString(), typeName);

                        await scope.CompleteAsync();

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Could not publish the integration event...", ex);
                        throw;
                    }
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR Handling transaction for {CommandName} ({@Command})", typeName, request);

            throw;
        }
    }
}
