using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;

namespace Microsoft.eShopOnContainers.Services.Locations.API;

public static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, LocationSettings locationSettings)
    {
        services.AddOpenTelemetry()
           .ConfigureResource(b =>
           {
               b.AddService(locationSettings.EventBus.EndpointName);
           })
           .WithTracing(b =>
           {
               if (locationSettings.UseAWS && !locationSettings.LocalStack.UseLocalStack)
               {
                   b.AddXRayTraceId()
                   .AddAWSInstrumentation();
               }

               b.AddAspNetCoreInstrumentation()
                  .AddHttpClientInstrumentation()
                  .AddGrpcCoreInstrumentation()
                  .AddEntityFrameworkCoreInstrumentation()
                  .AddOtlpExporter(options => options.Endpoint = new Uri(locationSettings.OtlpEndpoint));

               Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
           })
           .WithMetrics(b => b
               .AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddRuntimeInstrumentation()
               .AddProcessInstrumentation()
               .AddPrometheusExporter());

        return services;
    }
}

