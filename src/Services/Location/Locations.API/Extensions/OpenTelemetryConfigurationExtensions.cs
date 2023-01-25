using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rebus.OpenTelemetry.Configuration;
using System;

namespace Microsoft.eShopOnContainers.Services.Locations.API;

public static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, LocationSettings locationSettings)
    {
        services.AddOpenTelemetryTracing(builder =>
        {
            var traceProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(locationSettings.EventBus.EndpointName));

            if (locationSettings.UseAWS && !locationSettings.LocalStack.UseLocalStack)
            {
                traceProviderBuilder.AddXRayTraceId()
                .AddAWSInstrumentation();
            }

            var jaegerHost = locationSettings.Jaeger.Host;
            var jaegerPort = locationSettings.Jaeger.Port;

            if (!string.IsNullOrEmpty(jaegerHost))
            {
                traceProviderBuilder.AddJaegerExporter(options =>
                {
                    options.AgentHost = jaegerHost;
                    options.AgentPort = jaegerPort;
                    options.ExportProcessorType = ExportProcessorType.Simple;
                });
            }

            traceProviderBuilder.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddRebusInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(locationSettings.OtlpEndpoint);
                });


            Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
        });

        services.AddOpenTelemetryMetrics(builder =>
        {
            var meterProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                   .AddService(locationSettings.EventBus.EndpointName))
            .AddHttpClientInstrumentation()
            .AddAspNetCoreInstrumentation();

            meterProviderBuilder.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(locationSettings.OtlpEndpoint);
            });
        });

        return services;
    }
}

