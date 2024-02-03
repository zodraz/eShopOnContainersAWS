namespace Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure
{
    using Amazon.DynamoDBv2;
    using Amazon.DynamoDBv2.DataModel;
    using Amazon.DynamoDBv2.Model;
    using Microsoft.eShopOnContainers.Services.Marketing.API.Model;
    using Microsoft.Extensions.Logging;
    using Polly;
    using Polly.Retry;
    using System;
    using System.Collections.Generic;
    using System.Data.SqlClient;
    using System.Linq;
    using System.Threading.Tasks;

    public class MarketingContextSeed
    {
        public async Task SeedAsync(MarketingContext context,ILogger<MarketingContextSeed> logger, MarketingSettings marketingSettings, int retries = 3)
        {
            var policy = CreatePolicy(retries, logger, nameof(MarketingContextSeed));

            await policy.ExecuteAsync(async () =>
            {
                var dynamoDbConfig = new AmazonDynamoDBConfig
                {
                    RegionEndpoint = marketingSettings.AWSOptions.Region
                };

                var client = new AmazonDynamoDBClient(dynamoDbConfig);

                if (marketingSettings.LocalStack.UseLocalStack)
                {
                    dynamoDbConfig.ServiceURL = marketingSettings.LocalStack.LocalStackUrl;
                }

                await CreateTableIfNeeded(client);

                if (!context.Campaigns.Any())
                {
                    await context.Campaigns.AddRangeAsync(
                        GetPreconfiguredMarketings());

                    await context.SaveChangesAsync();
                }
            });
        }

        private List<Campaign> GetPreconfiguredMarketings()
        {
            return new List<Campaign>
            {
                new Campaign
                {
                    Name = ".NET Bot Black Hoodie 50% OFF",
                    Description = "Campaign Description 1",
                    From = DateTime.Now,
                    To = DateTime.Now.AddDays(365),
                    PictureUri = "http://externalcatalogbaseurltobereplaced/api/v1/campaigns/1/pic",
                    PictureName = "1.png",
                    Rules = new List<Rule>
                    {
                        new UserLocationRule
                        {
                            Description = "Campaign is only for United States users.",
                            LocationId = 1
                        }
                    }
                },
                new Campaign
                {
                    Name = "Roslyn Red T-Shirt 3x2",
                    Description = "Campaign Description 2",
                    From = DateTime.Now.AddDays(-7),
                    To = DateTime.Now.AddDays(365),
                    PictureUri = "http://externalcatalogbaseurltobereplaced/api/v1/campaigns/2/pic",
                    PictureName = "2.png",
                    Rules = new List<Rule>
                    {
                        new UserLocationRule
                        {
                            Description = "Campaign is only for Seattle users.",
                            LocationId = 3
                        }
                    }
                }
            };
        }
     
        private AsyncRetryPolicy CreatePolicy(int retries, ILogger<MarketingContextSeed> logger, string prefix)
        {
            return Policy.Handle<SqlException>().
                WaitAndRetryAsync(
                    retryCount: retries,
                    sleepDurationProvider: retry => TimeSpan.FromSeconds(5),
                    onRetry: (exception, timeSpan, retry, ctx) =>
                    {
                        logger.LogWarning(exception, "[{prefix}] Exception {ExceptionType} with message {Message} detected on attempt {retry} of {retries}", prefix, exception.GetType().Name, exception.Message, retry, retries);
                    }
                );
        }

        private async Task CreateTableIfNeeded(AmazonDynamoDBClient client)
        {
            // Initial value for the first page of table names.

            bool exists = await ExistsTable(client, "MarketingData");

            if (!exists)
            {
                var request = new CreateTableRequest
                {
                    TableName = "MarketingData",
                    AttributeDefinitions = new List<AttributeDefinition>()
                    {
                        new AttributeDefinition
                        {
                          AttributeName = "UserId",
                          AttributeType = "S"
                        }
                    },
                    KeySchema = new List<KeySchemaElement>()
                    {
                        new KeySchemaElement
                        {
                            AttributeName = "UserId",
                            KeyType = "HASH"  //Partition key
                        }
                    },
                    ProvisionedThroughput = new ProvisionedThroughput
                    {
                        ReadCapacityUnits = 10,
                        WriteCapacityUnits = 5
                    }
                };

                var response = await client.CreateTableAsync(request);
            }
        }

        private async Task<bool> ExistsTable(AmazonDynamoDBClient client, string table)
        {
            string lastEvaluatedTableName = null;
            bool exists;
            do
            {
                // Create a request object to specify optional parameters.
                var request = new ListTablesRequest
                {
                    Limit = 10, // Page size.
                    ExclusiveStartTableName = lastEvaluatedTableName
                };

                var response = await client.ListTablesAsync(request);
                var result = response.TableNames;
                exists = result.Contains(table);
                lastEvaluatedTableName = response.LastEvaluatedTableName;

            } while (lastEvaluatedTableName != null && !exists);
            return exists;
        }
    }
}
