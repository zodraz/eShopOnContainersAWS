namespace FunctionalTests.Services.Ordering;

using Autofac.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.eShopOnContainers.Services.Ordering.API;

public class OrderingTestsStartup : Startup
{
    public OrderingTestsStartup(IConfiguration configuration, IWebHostEnvironment env) : base(configuration, env)
    {
    }

    //public override IServiceProvider ConfigureServices(IServiceCollection services)
    //{
    //    services.Configure<EventBusSettings>(Configuration.GetSection("EventBus"));

    //    return base.ConfigureServices(services);
    //}

    protected override void ConfigureAuth(IApplicationBuilder app)
    {
        if (Configuration["isTest"] == bool.TrueString.ToLowerInvariant())
        {
            app.UseMiddleware<AutoAuthorizeMiddleware>();
            app.UseAuthorization();
        }
        else
        {
            base.ConfigureAuth(app);
        }
    }
}
