using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.eShopOnContainers.Services.Marketing.API.Model;
using System;

namespace Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure
{
    public class LocationsConverter : IPropertyConverter
    {
        public DynamoDBEntry ToEntry(object value)
        {
            if (value == null)
                return new Primitive { Value = null };

            Location item = value as Location;
            if (item == null)
                throw new InvalidCastException("Cannot convert Location to DynamoDBEntry.");

            string data = string.Format("{0};{1};{2}", item.LocationId, item.Code, item.Description);

            DynamoDBEntry entry = new Primitive { Value = data };

            return entry;
        }

        public object FromEntry(DynamoDBEntry entry)
        {
            if (entry == null)
                return new Location();

            Primitive primitive = entry as Primitive;
            if (primitive == null || !(primitive.Value is string) || string.IsNullOrEmpty((string)primitive.Value))
                throw new InvalidCastException("Cannot convert DynamoDBEntry to Location.");

            string[] data = ((string)(primitive.Value)).Split(new string[] { ";" }, StringSplitOptions.None);
            if (data.Length != 3)
                throw new ArgumentOutOfRangeException("Invalid arguments number.");

            Location complexData = new Location
            {
                LocationId = Convert.ToInt32(data[0]),
                Code = Convert.ToString(data[1]),
                Description = Convert.ToString(data[2]),
            };

            return complexData;
        }
    }
}