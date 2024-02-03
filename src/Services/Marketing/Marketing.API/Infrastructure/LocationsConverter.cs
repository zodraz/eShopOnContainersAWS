using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.eShopOnContainers.Services.Marketing.API.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure
{
    public class LocationsConverter : IPropertyConverter
    {
        public object FromEntry(DynamoDBEntry entry)
        {
            Primitive primitive = entry as Primitive;
            if (primitive == null) return new List<Location>();


            if (primitive.Type != DynamoDBEntryType.String)
            {
                throw new InvalidCastException(string.Format("LocationsConverter cannot be converted as its type is {0} with a value of {1}"
                    , primitive.Type, primitive.Value));
            }


            string json = primitive.AsString();
            return JsonConvert.DeserializeObject<List<Location>>(json);
        }

        public DynamoDBEntry ToEntry(object value)
        {
            if (value == null)
                return new Primitive { Value = null };

            List<Location> listItems = value as List<Location>;

            if (listItems == null)
                return null;

            string json = JsonConvert.SerializeObject(listItems);
            return new Primitive(json);
        }
    }
}