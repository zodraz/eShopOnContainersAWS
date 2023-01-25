using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.eShopOnContainers.Services.Marketing.API.Model;
using System.Threading.Tasks;

namespace Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure.Repositories
{
    public class DynamoDbMarketingDataRepository : IMarketingDataRepository
    {
        private readonly IDynamoDBContext _context;

        public DynamoDbMarketingDataRepository(MarketingSettings marketingSettings)
        {
            var dynamoDbConfig = new AmazonDynamoDBConfig
            {
                RegionEndpoint = marketingSettings.AWSOptions.Region
            };

            if (marketingSettings.LocalStack.UseLocalStack)
            {
                dynamoDbConfig.ServiceURL = marketingSettings.LocalStack.LocalStackUrl;
            }

            var client = new AmazonDynamoDBClient(dynamoDbConfig);
            _context = new DynamoDBContext(client);
        }

        public async Task<MarketingData> GetAsync(string userId)
        {
            return await _context.LoadAsync<MarketingData>(userId);
        }

        public async Task UpdateLocationAsync(MarketingData marketingData)
        {
            await _context.SaveAsync(marketingData);
        }
    }
}
