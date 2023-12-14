using Amazon.Runtime.Internal.Util;

namespace Microsoft.eShopOnContainers.Services.Catalog.API;

public class Startup
{
    private readonly IWebHostEnvironment _currentEnvironment;
    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        _currentEnvironment = env;
    }

    public IConfiguration Configuration { get; }

    public IServiceProvider ConfigureServices(IServiceCollection services)
    {
        var catalogSettings = Configuration.Get<CatalogSettings>();
        var eventBusSettings = catalogSettings.EventBus;

        services.AddOpenTelemetry(Configuration, eventBusSettings);

        services.AddGrpc().Services
            .AddCustomMVC(Configuration)
            .AddCustomDbContext(Configuration)
            .AddCustomOptions(Configuration)
            .AddIntegrationServices()
            .AddEventBus(Configuration, eventBusSettings)
            .AddSwagger(Configuration)
            .AddCustomHealthCheck(Configuration, eventBusSettings, _currentEnvironment);

        var container = new ContainerBuilder();
        container.Populate(services);

        container.RegisterHandlersFromAssemblyOf(typeof(OrderStatusChangedToPaidIntegrationEventHandler));

        return new AutofacServiceProvider(container.Build());
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
    {
        var pathBase = Configuration["PATH_BASE"];

        if (!string.IsNullOrEmpty(pathBase))
        {
            loggerFactory.CreateLogger<Startup>().LogDebug("Using PATH BASE '{pathBase}'", pathBase);
            app.UsePathBase(pathBase);
        }

        app.UseOpenTelemetryPrometheusScrapingEndpoint();

        app.UseSwagger()
            .UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "Catalog.API V1");
            });

        app.UseRouting();
        app.UseCors("CorsPolicy");
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapDefaultControllerRoute();
            endpoints.MapControllers();
            endpoints.MapGet("/_proto/", async ctx =>
            {
                ctx.Response.ContentType = "text/plain";
                using var fs = new FileStream(Path.Combine(env.ContentRootPath, "Proto", "catalog.proto"), FileMode.Open, FileAccess.Read);
                using var sr = new StreamReader(fs);
                while (!sr.EndOfStream)
                {
                    var line = await sr.ReadLineAsync();
                    if (line != "/* >>" || line != "<< */")
                    {
                        await ctx.Response.WriteAsync(line);
                    }
                }
            });
            endpoints.MapGrpcService<CatalogService>();
            endpoints.MapHealthChecks("/hc", new HealthCheckOptions()
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });
        });
    }
}

public static class CustomExtensionMethods
{
    private const string eventSufix = "IntegrationEvents-";

    public static IServiceCollection AddCustomMVC(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options =>
        {
            options.Filters.Add(typeof(HttpGlobalExceptionFilter));
        })
        .AddJsonOptions(options => options.JsonSerializerOptions.WriteIndented = true);

        services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy",
                builder => builder
                .SetIsOriginAllowed((host) => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());
        });

        return services;
    }
    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services, IConfiguration configuration,
        EventBusSettings eventBusSettings, IWebHostEnvironment env)
    {
        var awsOptions = configuration.GetAWSOptions();
        var useLocalStack = bool.Parse(configuration["LocalStack:UseLocalStack"]);
        var localStackUrl = configuration["LocalStack:LocalStackUrl"];
        var useAWS = bool.Parse(configuration["UseAWS"]);
        var useVault = bool.Parse(configuration["UseVault"]);
        var bucketName = configuration.GetValue<string>("S3BucketName");

        var hcBuilder = services.AddHealthChecks();

        hcBuilder
            .AddCheck("self", () => HealthCheckResult.Healthy())
            .AddSqlServer(
                configuration["ConnectionString"],
                name: "CatalogDB-check",
                tags: new string[] { "catalogdb" });

        if (!string.IsNullOrEmpty(bucketName) && useAWS)
        {
            hcBuilder
                .AddS3(options =>
                {
                    options.BucketName = bucketName;
                    options.S3Config = new Amazon.S3.AmazonS3Config();
                    if (useLocalStack)
                    {
                        options.S3Config.ServiceURL = localStackUrl;
                    }
                    else
                    {
                        options.S3Config.RegionEndpoint = awsOptions.Region;
                    }

                }, name: "catalog-storage-check", tags: new string[] { "catalogstorage" });
        }

        if (eventBusSettings.RabbitMQEnabled)
        {
            //var protocol = eventBusSettings.IsSecure ? "amqps" : "amqp";
            //var credentials = string.Empty;

            //if (!string.IsNullOrEmpty(eventBusSettings.UserName) && !string.IsNullOrEmpty(eventBusSettings.Password))
            //{
            //    credentials = $"{eventBusSettings.UserName}:{eventBusSettings.Password}@";
            //}

            //hcBuilder
            //    .AddRabbitMQ(
            //        $"{protocol}://{credentials}{eventBusSettings.Host}",
            //        name: "catalog-rabbitmqbus-check",
            //        tags: new string[] { "rabbitmqbus" });
        }
        else
        {
            if (useAWS && !useLocalStack)
            {
                hcBuilder
                      .AddSqs(options =>
                      {
                          options.RegionEndpoint = awsOptions.Region;
                          options.AddQueue(eventBusSettings.EndpointName);
                          if (eventBusSettings.AuditEnabled)
                          {
                              options.AddQueue("Audit");
                          }
                      });

                hcBuilder
                      .AddSnsTopicsAndSubscriptions(options =>
                      {
                          options.RegionEndpoint = awsOptions.Region;
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStockConfirmedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStockRejectedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(ProductPriceChangedIntegrationEvent)}");
                      });
            }
        }

        if (useVault && useAWS && !useLocalStack)
        {
            var settings = new VaultSettings();
            configuration.GetSection("Vault").Bind(settings);

            var secrets = settings.SecretGroups.Select(grp => $"{env.EnvironmentName}/{grp}").ToList();

            hcBuilder
                   .AddSecretsManager(options =>
                   {
                       options.RegionEndpoint = awsOptions.Region;
                       foreach (var secret in secrets)
                       {
                           options.AddSecret(secret);
                       }
                   });
        }

        return services;
    }

    public static IServiceCollection AddCustomDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEntityFrameworkSqlServer()
            .AddDbContext<CatalogContext>(options =>
            {
                options.UseSqlServer(configuration["ConnectionString"],
                                        sqlServerOptionsAction: sqlOptions =>
                                        {
                                            sqlOptions.MigrationsAssembly(typeof(Startup).GetTypeInfo().Assembly.GetName().Name);
                                            //Configuring Connection Resiliency: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency 
                                            sqlOptions.EnableRetryOnFailure(maxRetryCount: 15, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                                        });
            });

        return services;
    }

    public static IServiceCollection AddCustomOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CatalogSettings>(configuration);
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problemDetails = new ValidationProblemDetails(context.ModelState)
                {
                    Instance = context.HttpContext.Request.Path,
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Please refer to the errors property for additional details."
                };

                return new BadRequestObjectResult(problemDetails)
                {
                    ContentTypes = { "application/problem+json", "application/problem+xml" }
                };
            };
        });

        return services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSwaggerGen(options =>
        {         
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "eShopOnContainers - Catalog HTTP API",
                Version = "v1",
                Description = "The Catalog Microservice HTTP API. This is a Data-Driven/CRUD microservice sample"
            });
        });

        return services;
    }

    public static IServiceCollection AddIntegrationServices(this IServiceCollection services)
    {
        services.AddTransient<ICatalogIntegrationEventService, CatalogIntegrationEventService>();

        return services;
    }

    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration, EventBusSettings eventBusSettings)
    {
        if (eventBusSettings.RabbitMQEnabled)
        {
            var protocol = eventBusSettings.IsSecure ? "amqps" : "amqp";
            var port = eventBusSettings.IsSecure ? 5671 : 5672;
            var credentials = string.Empty;

            if (!string.IsNullOrEmpty(eventBusSettings.UserName) && !string.IsNullOrEmpty(eventBusSettings.Password))
            {
                credentials = $"{eventBusSettings.UserName}:{eventBusSettings.Password}@";
            }

            var rabbitConnectionString = $"{protocol}://{credentials}{eventBusSettings.Host}:{port}";

            List<ConnectionEndpoint> serverConnectionEndpoints = new()
            {
                new ConnectionEndpoint
                {
                    ConnectionString = rabbitConnectionString,
                    SslSettings = new SslSettings(eventBusSettings.IsSecure, eventBusSettings.Host,
                                                  version: System.Security.Authentication.SslProtocols.None)
                }
            };

            services.AddRebus((configure, provider) =>
            {
                var rebusConfig = configure
                    .Logging(l => l.Serilog())
                    .Transport(t => t.UseRabbitMq(serverConnectionEndpoints, eventBusSettings.EndpointName))
                    .UseSharedNothingApproach(builder => builder
                         .AddWithCustomName<OrderStatusChangedToAwaitingValidationIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                         .AddWithCustomName<OrderStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStockConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderStockRejectedIntegrationEvent>($"{eventSufix}{nameof(OrderStockRejectedIntegrationEvent)}")
                         .AddWithCustomName<ProductPriceChangedIntegrationEvent>($"{eventSufix}{nameof(ProductPriceChangedIntegrationEvent)}"))
                    .Options(t =>
                    {
                        if (eventBusSettings.FleetManager.Enabled)
                        {
                            var settings = new FleetManagerSettings(
                                messageAuditingLevel: MessageAuditingLevel.Full
                            );

                            t.EnableFleetManager(eventBusSettings.FleetManager.Url, eventBusSettings.FleetManager.ApiKey, settings);
                        }
                        t.SimpleRetryStrategy(errorQueueAddress: "Error", maxDeliveryAttempts: eventBusSettings.RetryCount);
                        t.EnableDiagnosticSources();
                    });

                if (eventBusSettings.OutboxEnabled)
                {
                    rebusConfig.Outbox(o => o.StoreInSqlServer(configuration["ConnectionString"], "Outbox"));
                }
                if (eventBusSettings.AuditEnabled)
                {
                    rebusConfig.Options(t => t.EnableMessageAuditing(auditQueue: "Audit"));
                }

                return rebusConfig;
            },
            onCreated: async bus =>
            {
                await bus.Subscribe<OrderStatusChangedToAwaitingValidationIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToPaidIntegrationEvent>();
            });
        }
        else
        {
            var awsOptions = configuration.GetAWSOptions();
            var useLocalStack = bool.Parse(configuration["LocalStack:UseLocalStack"]);
            var localStackUrl = configuration["LocalStack:LocalStackUrl"];

            AmazonSQSConfig amazonSqsConfig = new AmazonSQSConfig
            {
                RegionEndpoint = awsOptions.Region,
            };

            AmazonSimpleNotificationServiceConfig amazonSimpleNotificationServiceConfig = new AmazonSimpleNotificationServiceConfig
            {
                RegionEndpoint = awsOptions.Region,
            };

            if (useLocalStack)
            {
                amazonSqsConfig.ServiceURL = localStackUrl;
                amazonSimpleNotificationServiceConfig.ServiceURL = localStackUrl;
            }

            services.AddRebus((configure, provider) =>
            {
                var rebusConfig = configure
                    .Logging(l => l.Serilog())
                    .Transport(t => t.UseAmazonSnsAndSqs(workerQueueAddress: eventBusSettings.EndpointName,
                               amazonSqsConfig: amazonSqsConfig,
                               amazonSimpleNotificationServiceConfig: amazonSimpleNotificationServiceConfig))
                    .UseSharedNothingApproach(builder => builder
                         .AddWithCustomName<OrderStatusChangedToAwaitingValidationIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                         .AddWithCustomName<OrderStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStockConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderStockRejectedIntegrationEvent>($"{eventSufix}{nameof(OrderStockRejectedIntegrationEvent)}")
                         .AddWithCustomName<ProductPriceChangedIntegrationEvent>($"{eventSufix}{nameof(ProductPriceChangedIntegrationEvent)}"))
                    .Options(t =>
                    {
                        if (eventBusSettings.FleetManager.Enabled)
                        {
                            var settings = new FleetManagerSettings(
                                messageAuditingLevel: MessageAuditingLevel.Full
                            );

                            t.EnableFleetManager(eventBusSettings.FleetManager.Url, eventBusSettings.FleetManager.ApiKey, settings);
                        }
                        t.SimpleRetryStrategy(errorQueueAddress: "Error", maxDeliveryAttempts: eventBusSettings.RetryCount);
                        t.EnableDiagnosticSources();
                    });

                if (eventBusSettings.OutboxEnabled)
                {
                    rebusConfig.Outbox(o => o.StoreInSqlServer(configuration["ConnectionString"], "Outbox"));
                }
                if (eventBusSettings.AuditEnabled)
                {
                    rebusConfig.Options(t => t.EnableMessageAuditing(auditQueue: "Audit"));
                }

                return rebusConfig;
            },
            onCreated: async bus =>
            {
                await bus.Subscribe<OrderStatusChangedToAwaitingValidationIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToPaidIntegrationEvent>();
            });
        }

        return services;
    }
}