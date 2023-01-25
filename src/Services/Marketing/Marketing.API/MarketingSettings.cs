using Amazon.Extensions.NETCore.Setup;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;

namespace Microsoft.eShopOnContainers.Services.Marketing.API
{
    public class MarketingSettings
    {
        public string ConnectionString { get; set; }
        public string MongoConnectionString { get; set; }
        public string MongoDatabase { get; set; }
        public string ExternalCatalogBaseUrl { get; set; }
        public string CampaignDetailFunctionUri { get; set; }
        public string PicBaseUrl { get; set; }
        public bool S3Enabled { get; set; }
        public bool UseAWS { get; set; }
        public bool UseDocumentDb { get; set; }
        public AWSOptions AWSOptions { get; set; }
        public LocalStack LocalStack { get; set; }
        public EventBusSettings EventBus { get; set; }
    }

    public class LocalStack
    {
        public bool UseLocalStack { get; set; }
        public string LocalStackUrl { get; set; }
    }
}
