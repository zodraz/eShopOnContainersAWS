namespace Ordering.BackgroundTasks
{
    using HealthChecks.UI.Client;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
    using Microsoft.eShopOnContainers.Services.Ordering.API;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Ordering.BackgroundTasks.Extensions;
    using Ordering.BackgroundTasks.Services;
    using Prometheus;

    public class Startup
    {
        private readonly IHostEnvironment _currentEnvironment;
        public Startup(IConfiguration configuration, IHostEnvironment env)
        {
            Configuration = configuration;
            _currentEnvironment = env;
        }

        public IConfiguration Configuration { get; }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            var orderingBkgSettings = Configuration.Get<OrderingBkgSettings>();
            orderingBkgSettings.AWSOptions = Configuration.GetAWSOptions();
            var eventBusSettings = orderingBkgSettings.EventBus;

            services.AddOpenTelemetry(orderingBkgSettings);

            services.AddCustomHealthCheck(orderingBkgSettings, _currentEnvironment)
                .Configure<BackgroundTaskSettings>(this.Configuration)
                .AddOptions()
                .AddHostedService<GracePeriodManagerService>()
                .AddEventBus(orderingBkgSettings);
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            app.UseRouting();
            app.UseHttpMetrics(options =>
            {
                options.AddCustomLabel("host", context => context.Request.Host.Host);
            });
            app.UseHealthChecks("/hc", new HealthCheckOptions()
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            app.UseHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
            });
        }
    }
}
