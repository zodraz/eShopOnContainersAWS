using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Locations.API.Model.Core;
using Newtonsoft.Json;
using System;

namespace Microsoft.eShopOnContainers.Services.Locations.API.Model
{
	public class LocationPointConverter : IPropertyConverter
	{
		public object FromEntry(DynamoDBEntry entry)
		{
			Primitive primitive = entry as Primitive;
			if (primitive == null) return new LocationPoint();


			if (primitive.Type != DynamoDBEntryType.String)
			{
				throw new InvalidCastException(string.Format("LocationPoint cannot be converted as its type is {0} with a value of {1}"
					, primitive.Type, primitive.Value));
			}


			string json = primitive.AsString();
			return JsonConvert.DeserializeObject<LocationPoint>(json);
		}


		public DynamoDBEntry ToEntry(object value)
		{
			LocationPoint locationPointConverter = value as LocationPoint;
			if (locationPointConverter == null) return null;


			string json = JsonConvert.SerializeObject(locationPointConverter);
			return new Primitive(json);
		}
	}
}