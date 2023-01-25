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

namespace Microsoft.eShopOnContainers.Services.Marketing.API;

public static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, IConfiguration configuration, EventBusSettings eventBusSettings)
    {
        services.AddOpenTelemetryTracing(builder =>
        {
            var traceProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(eventBusSettings.EndpointName));

            if (configuration.GetValue("UseAWS", true) && !configuration.GetValue("LocalStack:UseLocalStack", true))
            {
                traceProviderBuilder.AddXRayTraceId()
                .AddAWSInstrumentation();
            }

            var jaegerHost = configuration["Jaeger:Host"];
            var jaegerPort = configuration.GetValue("Jaeger:Port", 6831);

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
                    options.Endpoint = new Uri(configuration["OtlpEndpoint"]);
                });


            Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
        });

        services.AddOpenTelemetryMetrics(builder =>
        {
            var meterProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                   .AddService(eventBusSettings.EndpointName))
            .AddHttpClientInstrumentation()
            .AddAspNetCoreInstrumentation();

            meterProviderBuilder.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(configuration["OtlpEndpoint"]);
            });
        });

        return services;
    }
}
