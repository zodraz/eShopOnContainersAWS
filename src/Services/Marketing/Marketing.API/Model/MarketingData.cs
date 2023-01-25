using Amazon.DynamoDBv2.DataModel;
using Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace Microsoft.eShopOnContainers.Services.Marketing.API.Model
{
    [DynamoDBTable("MarketingData")]
    public class MarketingData
    {
        [BsonIgnoreIfDefault]
        [BsonRepresentation(BsonType.ObjectId)]
        [DynamoDBIgnore]
        public string Id { get; set; }
        [DynamoDBHashKey]
        public string UserId { get; set; }
        [DynamoDBProperty(typeof(LocationsConverter))]
        public List<Location> Locations { get; set; }
        [DynamoDBProperty]
        public DateTime UpdateDate { get; set; }
    }
}
