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
            services.AddOpenTelemetryTracing(builder =>
            {
                var traceProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(orderingBkgSettings.EventBus.EndpointName));

                if (orderingBkgSettings.UseAWS && !orderingBkgSettings.LocalStack.UseLocalStack)
                {
                    traceProviderBuilder.AddXRayTraceId()
                    .AddAWSInstrumentation();
                }

                var jaegerHost = orderingBkgSettings.Jaeger.Host;
                var jaegerPort = orderingBkgSettings.Jaeger.Port;

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
                    .AddRebusInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(orderingBkgSettings.OtlpEndpoint);
                    });


                Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
            });

            services.AddOpenTelemetryMetrics(builder =>
            {
                var meterProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                       .AddService(orderingBkgSettings.EventBus.EndpointName))
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation();

                meterProviderBuilder.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(orderingBkgSettings.OtlpEndpoint);
                });
            });

            return services;
        }
    }
}
