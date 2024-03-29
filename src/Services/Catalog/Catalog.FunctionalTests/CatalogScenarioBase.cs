namespace Catalog.FunctionalTests;

public class CatalogScenariosBase
{
    public TestServer CreateServer()
    {
        var path = Assembly.GetAssembly(typeof(CatalogScenariosBase))
            .Location;

        var hostBuilder = new WebHostBuilder()
            .UseContentRoot(Path.GetDirectoryName(path))
            .ConfigureAppConfiguration(cb =>
            {
                cb.AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables();
            })
            .UseStartup<Startup>();


        var testServer = new TestServer(hostBuilder);

        testServer.Host
            .MigrateDbContext<CatalogContext>((context, services) =>
            {
                var env = services.GetService<IWebHostEnvironment>();
                var settings = services.GetService<IOptions<CatalogSettings>>();
                var logger = services.GetService<ILogger<CatalogContextSeed>>();

                new CatalogContextSeed()
                .SeedAsync(context, env, settings, logger)
                .Wait();
            });

        return testServer;
    }

    public static class Get
    {
        private const int PageIndex = 0;
        private const int PageCount = 4;

        public static string Items(bool paginated = false)
        {
            return paginated
                ? "api/v1/catalog/items" + Paginated(PageIndex, PageCount)
                : "api/v1/catalog/items";
        }

        public static string ItemById(int id)
        {
            return $"api/v1/catalog/items/{id}";
        }

        public static string ItemByName(string name, bool paginated = false)
        {
            return paginated
                ? $"api/v1/catalog/items/withname/{name}" + Paginated(PageIndex, PageCount)
                : $"api/v1/catalog/items/withname/{name}";
        }

        public static string Types = "api/v1/catalog/catalogtypes";

        public static string Brands = "api/v1/catalog/catalogbrands";

        public static string Filtered(int catalogTypeId, int catalogBrandId, bool paginated = false)
        {
            return paginated
                ? $"api/v1/catalog/items/type/{catalogTypeId}/brand/{catalogBrandId}" + Paginated(PageIndex, PageCount)
                : $"api/v1/catalog/items/type/{catalogTypeId}/brand/{catalogBrandId}";
        }

        private static string Paginated(int pageIndex, int pageCount)
        {
            return $"?pageIndex={pageIndex}&pageSize={pageCount}";
        }
    }
}
