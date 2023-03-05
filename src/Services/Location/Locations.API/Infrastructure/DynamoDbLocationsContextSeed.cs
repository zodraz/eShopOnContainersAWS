namespace Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure
{
    using Amazon.DynamoDBv2;
    using Amazon.DynamoDBv2.DataModel;
    using Amazon.DynamoDBv2.Model;
    using global::Locations.API.Model.Core;
    using Microsoft.eShopOnContainers.Services.Locations.API.Model;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public class DynamoDbLocationsContextSeed
    {
        private static IDynamoDBContext ctx;
        public static async Task SeedAsync(LocationSettings locationSettings, ILoggerFactory loggerFactory)
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

            await CreateTableIfNeeded(client);

            ctx = new DynamoDBContext(client);
          
            var cancellationToken = new CancellationToken();

            var request = new ScanRequest("Locations") { Select = Select.COUNT };

            var result = await client.ScanAsync(request, cancellationToken);

            var totalCount = result.Count;

            if (totalCount == 0)
            {
                await SetNorthAmerica();
                await SetSouthAmerica();
                await SetAfrica();
                await SetEurope();
                await SetAsia();
                await SetAustralia();
                await SetBarcelonaLocations();
            }
        }

        static async Task SetNorthAmerica()
        {
            var us = new Locations()
            {
                Code = "NA",
                Description = "North America",
                LocationId = 1
            }; 
            us.SetLocation(-103.219329, 48.803281);
            us.SetArea(GetNorthAmericaPoligon());
            await ctx.SaveAsync(us);
            await SetUSLocations(us.LocationId);
        }

        static async Task SetUSLocations(int parentLocationId)
        {
            var us = new Locations()
            {
                Parent_Location_Id = parentLocationId,
                Code = "US",
                Description = "United States",
                LocationId = 2
            };
            us.SetLocation(-101.357386, 41.650455);
            us.SetArea(GetUSPoligon());
            await ctx.SaveAsync(us);
            await SetWashingtonLocations(us.LocationId);
        }

        static async Task SetWashingtonLocations(int parentLocationId)
        {
            var wht = new Locations()
            {
                Parent_Location_Id = parentLocationId,
                Code = "WHT",
                Description = "Washington",
                LocationId = 3
            };
            wht.SetLocation(-119.542781, 47.223652);
            wht.SetArea(GetWashingtonPoligon());
            await ctx.SaveAsync(wht);
            await SetSeattleLocations(wht.LocationId);
            await SetRedmondLocations(wht.LocationId);
        }

        static async Task SetSeattleLocations(int parentLocationId)
        {
            var stl = new Locations()
            {
                Parent_Location_Id = parentLocationId,
                Code = "SEAT",
                Description = "Seattle",
                LocationId = 4
            };
            stl.SetArea(GetSeattlePoligon());
            stl.SetLocation(-122.330747, 47.603111);
            await ctx.SaveAsync(stl);
        }

        static async Task SetRedmondLocations(int parentLocationId)
        {
            var rdm = new Locations()
            {
                Parent_Location_Id = parentLocationId,
                Code = "REDM",
                Description = "Redmond",
                LocationId = 5
            };
            rdm.SetLocation(-122.122887, 47.674961);
            rdm.SetArea(GetRedmondPoligon());
            await ctx.SaveAsync(rdm);
        }

        static async Task SetBarcelonaLocations()
        {
            var bcn = new Locations()
            {
                Code = "BCN",
                Description = "Barcelona",
                LocationId = 6
            };
            bcn.SetLocation(2.156453, 41.395226);
            bcn.SetArea(GetBarcelonaPoligon());
            await ctx.SaveAsync(bcn);
        }

        static async Task SetSouthAmerica()
        {
            var sa = new Locations()
            {
                Code = "SA",
                Description = "South America",
                LocationId = 7
            };
            sa.SetLocation(-60.328704, -16.809748); 
            sa.SetArea(GetSouthAmericaPoligon());
            await ctx.SaveAsync(sa);
        }

        static async Task SetAfrica()
        {
            var afc = new Locations()
            {
                Code = "AFC",
                Description = "Africa",
                LocationId = 8
            };
            afc.SetLocation(19.475383, 13.063667); 
            afc.SetArea(GetAfricaPoligon());
            await ctx.SaveAsync(afc);
        }

        static async Task SetEurope()
        {
            var eu = new Locations()
            {
                Code = "EU",
                Description = "Europe",
                LocationId = 9
            };
            eu.SetLocation(13.147258, 49.947844); 
            eu.SetArea(GetEuropePoligon());
            await ctx.SaveAsync(eu);
        }

        static async Task SetAsia()
        {
            var asa = new Locations()
            {
                Code = "AS",
                Description = "Asia",
                LocationId = 10
            };
            asa.SetLocation(97.522257, 56.069107);
            asa.SetArea(GetAsiaPoligon());
            await ctx.SaveAsync(asa);
        }

        static async Task SetAustralia()
        {
            var aus = new Locations()
            {
                Code = "AUS",
                Description = "Australia",
                LocationId = 11
            };
            aus.SetLocation(133.733195, -25.010726);
            aus.SetArea(GetAustraliaPoligon());
            await ctx.SaveAsync(aus);
        }

        static List<LocationPoint> GetNorthAmericaPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-168.07786, 68.80277),
                        new LocationPoint(-119.60378, 32.7561),
                        new LocationPoint(-116.01966, 28.94642),
                        new LocationPoint(-98.52265, 14.49378),
                        new LocationPoint(-71.18188, 34.96278),
                        new LocationPoint(-51.97606, 48.24377),
                        new LocationPoint(-75.39806, 72.93141),
                        new LocationPoint(-168.07786, 68.80277)
                     };
        }

        static List<LocationPoint> GetSouthAmericaPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-91.43724, 13.29007),
                        new LocationPoint(-87.96315, -27.15081),
                        new LocationPoint(-78.75404, -50.71852),
                        new LocationPoint(-59.14765, -58.50773),
                        new LocationPoint(-50.08813, -42.22419),
                        new LocationPoint(-37.21044, -22.56725),
                        new LocationPoint(-36.61675, -0.38594),
                        new LocationPoint(-44.46056, -16.6746),
                        new LocationPoint(-91.43724, 13.29007),
                     };
        }

        static List<LocationPoint> GetAfricaPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-12.68724, 34.05892),
                        new LocationPoint(-18.33301, 20.77313),
                        new LocationPoint(-14.13503, 6.21292),
                        new LocationPoint(1.40221, -14.23693),
                        new LocationPoint(22.41485, -35.55408),
                        new LocationPoint(51.86499, -25.39831),
                        new LocationPoint(53.49269, 4.59405),
                        new LocationPoint(35.102, 26.14685),
                        new LocationPoint(21.63319, 33.75767),
                        new LocationPoint(6.58235, 37.05665),
                        new LocationPoint(-12.68724, 34.05892),
                     };
        }

        static List<LocationPoint> GetEuropePoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-11.73143, 35.27646),
                        new LocationPoint(-10.84462, 35.25123),
                        new LocationPoint(-10.09927, 35.26833),
                        new LocationPoint(49.00838, 36.56984),
                        new LocationPoint(36.63837, 71.69807),
                        new LocationPoint(-10.88788, 61.13851),
                        new LocationPoint(-11.73143, 35.27646),
                     };
        }

        static List<LocationPoint> GetAsiaPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(31.1592, 45.91629),
                        new LocationPoint(32.046, 45.89479),
                        new LocationPoint(62.32261, -4.45013),
                        new LocationPoint(154.47713, 35.14525),
                        new LocationPoint(-166.70788, 68.62211),
                        new LocationPoint(70.38837, 75.89335),
                        new LocationPoint(32.00274, 67.23428),
                        new LocationPoint(31.1592, 45.91629),
                     };
        }

        static List<LocationPoint> GetAustraliaPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(100.76857, -45.74117),
                        new LocationPoint(101.65538, -45.76273),
                        new LocationPoint(167.08823, -50.66317),
                        new LocationPoint(174.16463, -34.62579),
                        new LocationPoint(160.94837, -5.01004),
                        new LocationPoint(139.29462, -7.86376),
                        new LocationPoint(101.61212, -11.44654),
                        new LocationPoint(100.76857, -45.74117),
                     };
        }

        static List<LocationPoint> GetUSPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-62.88205, 48.7985),
                        new LocationPoint(-129.3132, 48.76513),
                        new LocationPoint(-120.9496, 30.12256),
                        new LocationPoint(-111.3944, 30.87114),
                        new LocationPoint(-78.11975, 24.24979),
                        new LocationPoint(-62.88205, 48.7985)
                     };
        }

        static List<LocationPoint> GetSeattlePoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-122.36238,47.82929),
                        new LocationPoint(-122.42091,47.6337),
                        new LocationPoint(-122.37371,47.45224),
                        new LocationPoint(-122.20788,47.50259),
                        new LocationPoint(-122.26934,47.73644),
                        new LocationPoint(-122.36238,47.82929)
                     };
        }

        static List<LocationPoint> GetRedmondPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-122.15432, 47.73148),
                        new LocationPoint(-122.17673, 47.72559),
                        new LocationPoint(-122.16904, 47.67851),
                        new LocationPoint(-122.16136, 47.65036),
                        new LocationPoint(-122.15604, 47.62746),
                        new LocationPoint(-122.01562, 47.63463),
                        new LocationPoint(-122.04961, 47.74244),
                        new LocationPoint(-122.15432, 47.73148)
                     };
        }

        static List<LocationPoint> GetWashingtonPoligon()
        {
            return new List<LocationPoint>()
                     {
                        new LocationPoint(-124.68633, 48.8943),
                        new LocationPoint(-124.32962, 45.66613),
                        new LocationPoint(-116.73824, 45.93384),
                        new LocationPoint(-116.96912, 49.04282),
                        new LocationPoint(-124.68633, 48.8943)
                     };
        }

        static List<LocationPoint> GetBarcelonaPoligon()
        {
            return new List<LocationPoint>()
            {
                new LocationPoint(2.033879, 41.383858),
                new LocationPoint(2.113741, 41.419068),
                new LocationPoint(2.188778, 41.451153),
                new LocationPoint(2.235266, 41.418033),
                new LocationPoint(2.137101, 41.299536),
                new LocationPoint(2.033879, 41.383858)
            };
        }

        static async Task CreateTableIfNeeded(AmazonDynamoDBClient client)
        {
            // Initial value for the first page of table names.

            bool exists = await ExistsTable(client, "Locations");

            if (!exists)
            {
                var request = new CreateTableRequest
                {
                    TableName = "Locations",
                    AttributeDefinitions = new List<AttributeDefinition>()
                    {
                        new AttributeDefinition
                        {
                          AttributeName = "LocationId",
                          AttributeType = "N"
                        }
                    },
                    KeySchema = new List<KeySchemaElement>()
                    {
                        new KeySchemaElement
                        {
                            AttributeName = "LocationId",
                            KeyType = "HASH"  //Partition key
                        }
                    },
                    ProvisionedThroughput = new ProvisionedThroughput
                    {
                        ReadCapacityUnits = 10,
                        WriteCapacityUnits = 5
                    }
                };

                var response = await client.CreateTableAsync(request);
            }

            exists = await ExistsTable(client, "UserLocations");

            if (!exists)
            {
                var request = new CreateTableRequest
                {
                    TableName = "UserLocations",
                    AttributeDefinitions = new List<AttributeDefinition>()
                    {
                        new AttributeDefinition
                        {
                          AttributeName = "UserId",
                          AttributeType = "S"
                        },
                        //new AttributeDefinition
                        //{
                        //  AttributeName = "LocationId",
                        //  AttributeType = "N"
                        //}
                    },
                    KeySchema = new List<KeySchemaElement>()
                    {
                        new KeySchemaElement
                        {
                            AttributeName = "UserId",
                            KeyType = "HASH"  //Partition key
                        },
                        //new KeySchemaElement
                        //{
                        //    AttributeName = "LocationId",
                        //    KeyType = "RANGE"  //Range key
                        //}
                    },
                    ProvisionedThroughput = new ProvisionedThroughput
                    {
                        ReadCapacityUnits = 10,
                        WriteCapacityUnits = 5
                    }
                };

                var response = await client.CreateTableAsync(request);
            }
        }

        static async Task<bool> ExistsTable(AmazonDynamoDBClient client, string table)
        {
            string lastEvaluatedTableName = null;
            bool exists;
            do
            {
                // Create a request object to specify optional parameters.
                var request = new ListTablesRequest
                {
                    Limit = 10, // Page size.
                    ExclusiveStartTableName = lastEvaluatedTableName
                };

                var response = await client.ListTablesAsync(request);
                var result = response.TableNames;
                exists = result.Contains(table);
                lastEvaluatedTableName = response.LastEvaluatedTableName;

            } while (lastEvaluatedTableName != null && !exists);
            return exists;
        }
    }
}
