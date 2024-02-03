using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using AWS.Lambda.Powertools.Logging;
using AWS.Lambda.Powertools.Tracing;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Net;
using System.Reflection;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Marketing.Functions
{
    public class Function
    {
        private ILambdaConfiguration LambdaConfiguration { get; }

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Function()
        {
            AWSSDKHandler.RegisterXRayForAllServices();
            var services = new ServiceCollection();
            services.AddTransient<ILambdaConfiguration, LambdaConfiguration>();
            var serviceProvider = services.BuildServiceProvider();
            LambdaConfiguration = serviceProvider.GetRequiredService<ILambdaConfiguration>();
            AddOpenTelemetry();
        }

        private void AddOpenTelemetry()
        {
            var traceProviderBuilder = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Marketing.Function").AddTelemetrySdk())
                    .AddAspNetCoreInstrumentation()
                    .AddXRayTraceId()
                    .AddAWSInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = new Uri(LambdaConfiguration.Configuration["OtlpEndpoint"]));

            traceProviderBuilder.Build();

            Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());

            var meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddPrometheusExporter();

            meterProviderBuilder.Build();
        }


        /// <summary>
        /// A Lambda function to respond to HTTP Get methods from API Gateway
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The API Gateway response.</returns>
        [Logging(LogEvent = true)]
        [Tracing(CaptureMode = TracingCaptureMode.ResponseAndError)]
        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
        {
            var requestContextRequestId = request.RequestContext.RequestId;

            var lookupInfo = new Dictionary<string, object>()
            {
                {"LookupInfo", new Dictionary<string, object>{{ "LookupId", requestContextRequestId }}}
            };

            // Appended keys are added to all subsequent log entries in the current execution.
            // Call this method as early as possible in the Lambda handler.
            // Typically this is value would be passed into the function via the event.
            // Set the ClearState = true to force the removal of keys across invocations,
            Logger.AppendKeys(lookupInfo);

            Logger.LogInformation($"Campaign HTTP trigger function processed a request. RequestUri={request.RawPath}");

            string htmlResponse = string.Empty;

            // parse query parameter
            string campaignId = request.QueryStringParameters
                .FirstOrDefault(q => string.Compare(q.Key, "campaignId", true) == 0)
                .Value;

            string userId = request.QueryStringParameters
                .FirstOrDefault(q => string.Compare(q.Key, "userId", true) == 0)
                .Value;

            var cnnString = LambdaConfiguration.Configuration["ConnectionString"];

            // Trace Fluent API
            Tracing.WithSubsegment("LoggingResponse",
                subsegment =>
                {
                    subsegment.AddAnnotation("AccountId", request.RequestContext.AccountId);
                    subsegment.AddMetadata("campaignId", campaignId);
                    subsegment.AddMetadata("userId", userId);
                });
            try
            {
                htmlResponse = await GetCampaignHtml(htmlResponse, campaignId, cnnString);

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = htmlResponse,
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/html" } }
                };
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);

                return new APIGatewayProxyResponse
                {
                    Body = e.Message,
                    StatusCode = 500,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
        }

        [Tracing(SegmentName = "SQLServer")]
        private async Task<string> GetCampaignHtml(string htmlResponse, string campaignId, string cnnString)
        {
            try
            {
                using (var conn = new SqlConnection(cnnString))
                {
                    await conn.OpenAsync();
                    var sql = "SELECT * FROM [dbo].[Campaign] WHERE Id = @CampaignId;";
                    var campaign = (await conn.QueryAsync<Campaign>(sql, new { CampaignId = campaignId })).FirstOrDefault();
                    htmlResponse = BuildHtmlResponse(campaign);
                }

                return htmlResponse;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                throw;
            }
        }

        private string BuildHtmlResponse(Campaign campaign)
        {
            var marketingStorageUri = LambdaConfiguration.Configuration["MarketingStorageUri"];

            return string.Format(@"
      <html>
      <head>
        <link href='https://maxcdn.bootstrapcdn.com/bootstrap/4.0.0-alpha.6/css/bootstrap.min.css' rel='stylesheet'>
      </head>
      <header>
        <title>Campaign Details</title>
      </header>
      <body>
        <div class='container'>
          </br>
          <div class='card-deck'>
            <div class='card text-center'>
              <img class='card-img-top' src='{0}' alt='Card image cap'>
              <div class='card-block'>
                <h4 class='card-title'>{1}</h4>
                <p class='card-text'>{2}</p>
                <div class='card-footer'>
                  <small class='text-muted'>From {3} until {4}</small>
                </div>
              </div>
            </div>
          </div>
        </div>
      </body>
      </html>",
              $"{marketingStorageUri}{campaign.PictureName}",
              campaign.Name,
              campaign.Description,
              campaign.From.ToString("MMMM dd, yyyy"),
              campaign.From.ToString("MMMM dd, yyyy"));
        }

        public class Campaign
        {
            public int Id { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            public DateTime From { get; set; }

            public DateTime To { get; set; }

            public string PictureUri { get; set; }

            public string PictureName { get; set; }
        }
    }
}