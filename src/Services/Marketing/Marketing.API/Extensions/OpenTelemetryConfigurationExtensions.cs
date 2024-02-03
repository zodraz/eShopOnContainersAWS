﻿using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rebus.OpenTelemetry.Configuration;
using System;

namespace Microsoft.eShopOnContainers.Services.Marketing.API;

public static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, IConfiguration configuration, EventBusSettings eventBusSettings)
    {
        services.AddOpenTelemetry()
           .ConfigureResource(b =>
           {
               b.AddService(eventBusSettings.EndpointName);
           })
           .WithTracing(b =>
           {
               if (configuration.GetValue("UseAWS", true) && !configuration.GetValue("LocalStack:UseLocalStack", true))
               {
                   b.AddXRayTraceId()
                   .AddAWSInstrumentation();
               }

               b.AddAspNetCoreInstrumentation()
                  .AddHttpClientInstrumentation()
                  .AddRebusInstrumentation()
                  .AddEntityFrameworkCoreInstrumentation()
                  .AddOtlpExporter(options => options.Endpoint = new Uri(configuration["OtlpEndpoint"]));

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
