using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.eShopOnContainers.Services.Ordering.API;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rebus.OpenTelemetry.Configuration;
using System;


namespace Ordering.BackgroundTasks.Extensions
{
    public static class OpenTelemetryConfigurationExtensions
    {
        public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, OrderingBkgSettings orderingBkgSettings)
        {
            services.AddOpenTelemetry()
               .ConfigureResource(b =>
               {
                   b.AddService(orderingBkgSettings.EventBus.EndpointName);
               })
               .WithTracing(b =>
               {
                   if (orderingBkgSettings.UseAWS && !orderingBkgSettings.LocalStack.UseLocalStack)
                   {
                       b.AddXRayTraceId()
                       .AddAWSInstrumentation();
                   }

                   b.AddAspNetCoreInstrumentation()
                      .AddHttpClientInstrumentation()
                      .AddRebusInstrumentation()
                      .AddOtlpExporter(options => options.Endpoint = new Uri(orderingBkgSettings.OtlpEndpoint));

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
}
