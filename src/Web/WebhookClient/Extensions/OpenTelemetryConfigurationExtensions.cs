namespace WebhookClient.Extensions;

public static class OpenTelemetryConfigurationExtensions
{
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(b =>
            {
                b.AddService(Program.AppName);
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
