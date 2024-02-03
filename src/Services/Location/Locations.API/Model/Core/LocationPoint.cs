using Amazon.DynamoDBv2.DataModel;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace Locations.API.Model.Core
{
    public class LocationPoint
    {
        [DynamoDBProperty]
        [BsonIgnore]
        public double Latitude { get; set; }

        [DynamoDBProperty]
        [BsonIgnore]
        public double Longitude { get; set; }

        public LocationPoint()
        {
        }

        public LocationPoint(double longitude, double latitude)
        {
            this.coordinates.Add(longitude);
            this.coordinates.Add(latitude);

            Latitude = latitude;
            Longitude = longitude;
        }

        public string type { get; private set; } = "Point";

        public List<double> coordinates { get; private set; } = new List<double>();
    }
}
