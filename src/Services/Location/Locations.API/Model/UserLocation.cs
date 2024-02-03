namespace Microsoft.eShopOnContainers.Services.Locations.API.Model
{
    using Amazon.DynamoDBv2.DataModel;
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization.Attributes;
    using System;

    [DynamoDBTable("UserLocations")]
    public class UserLocation
    {
        [BsonIgnoreIfDefault]
        [BsonRepresentation(BsonType.ObjectId)]
        [DynamoDBIgnore]
        public string Id { get; set; }

        [DynamoDBHashKey]
        public string UserId { get; set; }

        //[DynamoDBRangeKey]
        [DynamoDBProperty]
        public int LocationId { get; set; }

        [DynamoDBProperty]
        public DateTime UpdateDate { get; set; }
    }
}
