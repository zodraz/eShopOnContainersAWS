namespace Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure.Repositories
{
    using Amazon.DynamoDBv2.DataModel;
    using Amazon.DynamoDBv2.DocumentModel;
    using Microsoft.eShopOnContainers.Services.Locations.API.Model;
    using Microsoft.Extensions.Options;
    using MongoDB.Driver;
    using MongoDB.Driver.GeoJsonObjectModel;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ViewModel;
    using global::Locations.API.Model.Core;
    using global::Locations.API.Extensions;
    using Amazon.DynamoDBv2;
    using Amazon;
    using global::Locations.API;
    using Amazon.Extensions.NETCore.Setup;
    using Amazon.DynamoDBv2.Model;

    public class DynamoDbLocationsRepository: ILocationsRepository
    {
        private readonly IDynamoDBContext _context;

        public DynamoDbLocationsRepository(LocationSettings locationSettings)
        {
            var dynamoDbConfig = new AmazonDynamoDBConfig
            {
                RegionEndpoint = locationSettings.AWSOptions.Region
            };

            var client = new AmazonDynamoDBClient(dynamoDbConfig);

            if (locationSettings.LocalStack.UseLocalStack)
            {
                dynamoDbConfig.ServiceURL = locationSettings.LocalStack.LocalStackUrl;
            }

            _context = new DynamoDBContext(client);
        }

        public async Task<Locations> GetAsync(int locationId)
        {
            return await _context.LoadAsync<Locations>(locationId);
        }

        public async Task<UserLocation> GetUserLocationAsync(string userId)
        {
            return await _context.LoadAsync<UserLocation>(userId);
        }

        public async Task<List<Locations>> GetLocationListAsync()
        {
            List<Locations> locations = new List<Locations>();

            var table = _context.GetTargetTable<Locations>();

            string paginationToken = "{}";
            do
            {
                var result = table.Scan(new ScanOperationConfig
                {
                    Limit = 20,
                    PaginationToken = paginationToken
                });
                var items = await result.GetNextSetAsync();
                paginationToken = result.PaginationToken;

                var locs = _context.FromDocuments<Locations>(items);

                locations.AddRange(locs);
            }
            while (!string.Equals(paginationToken, "{}", StringComparison.Ordinal));

            return locations;
        }     

        public async Task<List<Locations>> GetCurrentUserRegionsListAsync(LocationRequest currentPosition)
        {
            var locToSearch = new LocationPoint(currentPosition.Longitude, currentPosition.Latitude);
            var allLocations = await GetLocationListAsync();
            List<Locations> result = new List<Locations>();

            foreach (var location in allLocations)
            {
                if (locToSearch.IsInPolygon(location.AreaLocationPolygon))
                {
                    result.Add(location);
                }
            }

            return result;
        }        

        public async Task AddUserLocationAsync(UserLocation location)
        {
            await _context.SaveAsync(location);
        }

        public async Task UpdateUserLocationAsync(UserLocation userLocation)
        {
            await _context.SaveAsync(userLocation);
        }
    }
}
