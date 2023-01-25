namespace WebStatus;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();

        services.AddOptions();
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy())
            .ForwardToPrometheus();

        services
            .AddHealthChecksUI()
            .AddInMemoryStorage();

        services.AddMvc()
            .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);

        services.AddOpenTelemetry(Configuration);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
        }

        var pathBase = Configuration["PATH_BASE"];
        if (!string.IsNullOrEmpty(pathBase))
        {
            app.UsePathBase(pathBase);
        }

        app.UseHealthChecksUI(config =>
        {
            config.ResourcesPath = string.IsNullOrEmpty(pathBase) ? "/ui/resources" : $"{pathBase}/ui/resources";
            config.UIPath = "/hc-ui";
        });

        app.UseStaticFiles();

        app.UseRouting();
        app.UseHttpMetrics(options =>
        {
            options.AddCustomLabel("host", context => context.Request.Host.Host);
        });
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapMetrics();
            endpoints.MapDefaultControllerRoute();
            endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });
        });
    }
}
