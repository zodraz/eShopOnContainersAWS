namespace eShopConContainers.WebSPA.Extensions;

public static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetryTracing(builder =>
        {
            var traceProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(Program.AppName));

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
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(configuration["OtlpEndpoint"]);
                });


            Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
        });

        services.AddOpenTelemetryMetrics(builder =>
        {
            var meterProviderBuilder = builder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                   .AddService(Program.AppName))
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
