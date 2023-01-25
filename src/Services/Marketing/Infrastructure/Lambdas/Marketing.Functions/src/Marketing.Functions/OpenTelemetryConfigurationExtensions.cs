//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using OpenTelemetry;
//using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
//using OpenTelemetry.Resources;
//using OpenTelemetry.Trace;

//namespace Marketing.Functions;

//public static class OpenTelemetryConfigurationExtensions
//{
//    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services, ILambdaConfiguration lambdaConfiguration)
//    {
//        services.AddOpenTelemetryTracing(builder =>
//        {
//            builder.AddAspNetCoreInstrumentation()
//                .AddXRayTraceId()
//                .AddAWSInstrumentation()
//                .AddAspNetCoreInstrumentation()
//                .AddHttpClientInstrumentation()
//                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Marketing.Function").AddTelemetrySdk())
//                .AddOtlpExporter(options =>
//                {
//                   //options.Endpoint = new Uri(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
//                   options.Endpoint = new Uri("http://localhost:4317/");
//                });

//            Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
//        });

//        return services;
//    }
//}
