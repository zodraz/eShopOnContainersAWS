namespace Microsoft.eShopOnContainers.Services.Locations.API.Model
{
    using Amazon.DynamoDBv2.DataModel;
    using global::Locations.API.Model.Core;
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization.Attributes;
    using System.Collections.Generic;

    [DynamoDBTable("Locations")]
    public class Locations
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [DynamoDBIgnore]
        public string Id { get; set; }

        [DynamoDBHashKey]
        public int LocationId { get; set; }

        [DynamoDBProperty]
        public string Code { get; set; }

        
        [BsonRepresentation(BsonType.ObjectId)]
        [DynamoDBIgnore]
        public string Parent_Id { get; set; }

        [DynamoDBProperty]
        [BsonIgnore]
        public int Parent_Location_Id { get; set; }

        [DynamoDBProperty]
        public string Description { get; set; }

        [DynamoDBIgnore]
        public double Latitude { get; set; }
        [DynamoDBIgnore]
        public double Longitude { get; set; }

        [DynamoDBProperty(Converter = typeof(LocationPointConverter))]
        public LocationPoint Location { get; set; }

        [DynamoDBProperty(Converter = typeof(AreaLocationPolygonConverter))]
        [BsonIgnore]
        public AreaLocationPolygon AreaLocationPolygon { get; set; }

        [DynamoDBIgnore]
        public LocationPolygon Polygon { get; private set; }

        // Temporal commented in previewVersion7 of netcore and 2.9.0-beta2 of Mongo packages, review in next versions
        // public GeoJsonPoint<GeoJson2DGeographicCoordinates> Location { get; private set; }
        // public GeoJsonPolygon<GeoJson2DGeographicCoordinates> Polygon { get; private set; }
        public void SetLocation(double lon, double lat) => SetPosition(lon, lat);
        public void SetArea(List<LocationPoint> coordinatesList) => SetPolygon(coordinatesList);

        private void SetPosition(double lon, double lat)
        {
            Latitude = lat;
            Longitude = lon;
            Location = new LocationPoint(lon, lat);
        }

        private void SetPolygon(List<LocationPoint> coordinatesList)
        {
            Polygon = new LocationPolygon(coordinatesList);
            AreaLocationPolygon = new AreaLocationPolygon(coordinatesList);
        }
    }
}
