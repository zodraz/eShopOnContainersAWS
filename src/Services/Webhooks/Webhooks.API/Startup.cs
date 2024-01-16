namespace Webhooks.API;
public class Startup
{
    public IConfiguration Configuration { get; }

    private readonly IWebHostEnvironment _currentEnvironment;
    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        _currentEnvironment = env;
    }

    public IServiceProvider ConfigureServices(IServiceCollection services)
    {
        var webhookSettings = Configuration.Get<WebhooksSettings>();
        webhookSettings.AWSOptions = Configuration.GetAWSOptions();
        var eventBusSettings = webhookSettings.EventBus;

        services
            .AddCustomRouting(Configuration)
            .AddCustomDbContext(Configuration)
            .AddSwagger(Configuration)
            .AddCustomHealthCheck(Configuration, eventBusSettings, _currentEnvironment)
            .AddHttpClientServices(Configuration)
            .AddEventBus(Configuration, eventBusSettings)
            .AddCustomAuthentication(Configuration)
            .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
            .AddTransient<IIdentityService, IdentityService>()
            .AddTransient<IGrantUrlTesterService, GrantUrlTesterService>()
            .AddTransient<IWebhooksRetriever, WebhooksRetriever>()
            .AddTransient<IWebhooksSender, WebhooksSender>()
            .AddOpenTelemetry(Configuration, eventBusSettings);

        var container = new ContainerBuilder();
        container.Populate(services);
        container.RegisterHandlersFromAssemblyOf(typeof(OrderStatusChangedToPaidIntegrationEventHandler));

        return new AutofacServiceProvider(container.Build());
    }

    public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
    {        
        var pathBase = Configuration["PATH_BASE"];

        if (!string.IsNullOrEmpty(pathBase))
        {
            loggerFactory.CreateLogger("init").LogDebug("Using PATH BASE '{PathBase}'", pathBase);
            app.UsePathBase(pathBase);
        }

        app.UseOpenTelemetryPrometheusScrapingEndpoint();
        app.UseRouting();
        app.UseCors("CorsPolicy");
        app.UseHealthChecks("/hc", new HealthCheckOptions()
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
        app.UseHealthChecks("/liveness", new HealthCheckOptions
        {
            Predicate = r => r.Name.Contains("self")
        });
        ConfigureAuth(app);

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapDefaultControllerRoute();
            endpoints.MapControllers();
        });

        app.UseSwagger()
            .UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "Webhooks.API V1");
                c.OAuthClientId("webhooksswaggerui");
                c.OAuthAppName("WebHooks Service Swagger UI");
            });
    }

    protected virtual void ConfigureAuth(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}

static class CustomExtensionMethods
{
    private const string eventSufix = "IntegrationEvents-";

    public static IServiceCollection AddCustomRouting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options =>
        {
            options.Filters.Add(typeof(HttpGlobalExceptionFilter));
        });

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

    public static IServiceCollection AddCustomDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEntityFrameworkSqlServer()
            .AddDbContext<WebhooksContext>(options =>
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

    public static IServiceCollection AddSwagger(this IServiceCollection services, IConfiguration configuration)
    {
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
        {
            return services;
        }

        services.AddSwaggerGen(options =>
        {            
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "eShopOnContainers - Webhooks HTTP API",
                Version = "v1",
                Description = "The Webhooks Microservice HTTP API. This is a simple webhooks CRUD registration entrypoint"
            });

            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows()
                {
                    Implicit = new OpenApiOAuthFlow()
                    {
                        AuthorizationUrl = new Uri($"{configuration.GetValue<string>("IdentityUrlExternal")}/connect/authorize"),
                        TokenUrl = new Uri($"{configuration.GetValue<string>("IdentityUrlExternal")}/connect/token"),
                        Scopes = new Dictionary<string, string>()
                        {
                            {  "webhooks", "Webhooks API" }
                        }
                    }
                }
            });

            options.OperationFilter<AuthorizeCheckOperationFilter>();
        });

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
                        .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToShippedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}")
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
                await bus.Subscribe<OrderStatusChangedToPaidIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToShippedIntegrationEvent>();
                await bus.Subscribe<ProductPriceChangedIntegrationEvent>();
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
                        .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToShippedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}")
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
                await bus.Subscribe<OrderStatusChangedToPaidIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToShippedIntegrationEvent>();
                await bus.Subscribe<ProductPriceChangedIntegrationEvent>();
            });
        }

        return services;
    }

    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services, IConfiguration configuration,
        EventBusSettings eventBusSettings, IWebHostEnvironment env)
    {
        var awsOptions = configuration.GetAWSOptions();
        var useLocalStack = bool.Parse(configuration["LocalStack:UseLocalStack"]);
        var useAWS = bool.Parse(configuration["UseAWS"]);
        var useVault = bool.Parse(configuration["UseVault"]);

        var hcBuilder = services.AddHealthChecks();

        hcBuilder
            .AddCheck("self", () => HealthCheckResult.Healthy())
            .AddSqlServer(
                configuration["ConnectionString"],
                name: "WebhooksApiDb-check",
                tags: new string[] { "webhooksdb" });

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
            //        name: "signalr-rabbitmqbus-check",
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
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}");
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
    public static IServiceCollection AddHttpClientServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddHttpClient("extendedhandlerlifetime").SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        //add http client services
        services.AddHttpClient("GrantClient")
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        return services;
    }

    public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // prevent from mapping "sub" claim to nameidentifier.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

        var identityUrl = configuration.GetValue<string>("IdentityUrl");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

        }).AddJwtBearer(options =>
        {
            options.Authority = identityUrl;
            options.RequireHttpsMetadata = false;
            options.Audience = "webhooks";
        });

        return services;
    }
}
