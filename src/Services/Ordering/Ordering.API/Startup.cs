using Microsoft.eShopOnContainers.Services.Ordering.API.Application.IntegrationEvents;

namespace Microsoft.eShopOnContainers.Services.Ordering.API;

public class Startup
{
    private readonly IWebHostEnvironment CurrentEnvironment;
    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        CurrentEnvironment = env;
    }

    public IConfiguration Configuration { get; }

    public virtual IServiceProvider ConfigureServices(IServiceCollection services)
    {
        var orderingSettings = Configuration.Get<OrderingSettings>();
        orderingSettings.AWSOptions = Configuration.GetAWSOptions();
        var eventBusSettings = orderingSettings.EventBus;

        services.AddOpenTelemetry(Configuration, eventBusSettings);

        services
            .AddGrpc(options =>
            {
                options.EnableDetailedErrors = true;
            })
            .Services
            .AddCustomMvc()
            .AddCustomHealthCheck(Configuration, eventBusSettings, CurrentEnvironment)
            .AddCustomDbContext(Configuration)
            .AddCustomSwagger(Configuration)
            .AddCustomIntegrations(Configuration)
            .AddCustomConfiguration(Configuration)
            .AddEventBus(Configuration, eventBusSettings)
            .AddCustomAuthentication(Configuration);
        //configure autofac

        var container = new ContainerBuilder();
        container.Populate(services);

        container.RegisterModule(new MediatorModule());
        container.RegisterModule(new ApplicationModule(Configuration["ConnectionString"]));
        container.RegisterHandlersFromAssemblyOf(typeof(GracePeriodConfirmedIntegrationEventHandler));

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

        app.UseSwagger()
            .UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "Ordering.API V1");
                c.OAuthClientId("orderingswaggerui");
                c.OAuthAppName("Ordering Swagger UI");
            });

        app.UseRouting();
        app.UseHttpMetrics(options =>
        {
            options.AddCustomLabel("host", context => context.Request.Host.Host);
        });
        app.UseGrpcMetrics();
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
            endpoints.MapMetrics();
            endpoints.MapGrpcService<OrderingService>();
            endpoints.MapDefaultControllerRoute();
            endpoints.MapControllers();
            endpoints.MapGet("/_proto/", async ctx =>
            {
                ctx.Response.ContentType = "text/plain";
                using var fs = new FileStream(Path.Combine(env.ContentRootPath, "Proto", "basket.proto"), FileMode.Open, FileAccess.Read);
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
        });
    }

    protected virtual void ConfigureAuth(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}

static class CustomExtensionsMethods
{
    private const string eventSufix = "IntegrationEvents-";
    public static IServiceCollection AddCustomMvc(this IServiceCollection services)
    {
        // Add framework services.
        services.AddControllers(options =>
            {
                options.Filters.Add(typeof(HttpGlobalExceptionFilter));
            })
            // Added for functional tests
            .AddApplicationPart(typeof(OrdersController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.WriteIndented = true)
            .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);

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
        var useAWS = bool.Parse(configuration["UseAWS"]);
        var useVault = bool.Parse(configuration["UseVault"]);

        var hcBuilder = services.AddHealthChecks();

        hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

        hcBuilder
            .AddSqlServer(
                configuration["ConnectionString"],
                name: "OrderingDB-check",
                tags: new string[] { "orderingdb" });

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
            //        name: "basket-rabbitmqbus-check",
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
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(GracePeriodConfirmedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderPaymentFailedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderPaymentSucceededIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStartedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToCancelledIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToSubmittedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStockConfirmedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStockRejectedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(UserCheckoutAcceptedIntegrationEvent)}");
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

        hcBuilder.ForwardToPrometheus();

        return services;
    }

    public static IServiceCollection AddCustomDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrderingContext>(options =>
                {
                    options.UseSqlServer(configuration["ConnectionString"],
                        sqlServerOptionsAction: sqlOptions =>
                        {
                            sqlOptions.MigrationsAssembly(typeof(Startup).GetTypeInfo().Assembly.GetName().Name);
                            sqlOptions.EnableRetryOnFailure(maxRetryCount: 15, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                        });
                },
                    ServiceLifetime.Scoped  //Showing explicitly that the DbContext is shared across the HTTP request scope (graph of objects started in the HTTP request)
                );

        return services;
    }

    public static IServiceCollection AddCustomSwagger(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSwaggerGen(options =>
        {            
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "eShopOnContainers - Ordering HTTP API",
                Version = "v1",
                Description = "The Ordering Service HTTP API"
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
                            { "orders", "Ordering API" }
                        }
                    }
                }
            });

            options.OperationFilter<AuthorizeCheckOperationFilter>();
        });

        return services;
    }

    public static IServiceCollection AddCustomIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IIdentityService, IdentityService>();

        services.AddTransient<IOrderingIntegrationEventService, OrderingIntegrationEventService>();

        return services;
    }

    public static IServiceCollection AddCustomConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
        services.Configure<OrderingSettings>(configuration);
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

    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration, EventBusSettings eventBusSettings)
    {
        const string eventSufix = "IntegrationEvents-";

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
                         .AddWithCustomName<GracePeriodConfirmedIntegrationEvent>($"{eventSufix}{nameof(GracePeriodConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderPaymentFailedIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentFailedIntegrationEvent)}")
                         .AddWithCustomName<OrderPaymentSucceededIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentSucceededIntegrationEvent)}")
                         .AddWithCustomName<OrderStartedIntegrationEvent>($"{eventSufix}{nameof(OrderStartedIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToAwaitingValidationIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToCancelledIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToCancelledIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToShippedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToSubmittedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToSubmittedIntegrationEvent)}")
                         .AddWithCustomName<OrderStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStockConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderStockRejectedIntegrationEvent>($"{eventSufix}{nameof(OrderStockRejectedIntegrationEvent)}")
                         .AddWithCustomName<UserCheckoutAcceptedIntegrationEvent>($"{eventSufix}{nameof(UserCheckoutAcceptedIntegrationEvent)}"))
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
                await bus.Subscribe<GracePeriodConfirmedIntegrationEvent>();
                await bus.Subscribe<OrderPaymentFailedIntegrationEvent>();
                await bus.Subscribe<OrderPaymentSucceededIntegrationEvent>();
                await bus.Subscribe<OrderStockConfirmedIntegrationEvent>();
                await bus.Subscribe<OrderStockRejectedIntegrationEvent>();
                await bus.Subscribe<UserCheckoutAcceptedIntegrationEvent>();
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
                         .AddWithCustomName<GracePeriodConfirmedIntegrationEvent>($"{eventSufix}{nameof(GracePeriodConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderPaymentFailedIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentFailedIntegrationEvent)}")
                         .AddWithCustomName<OrderPaymentSucceededIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentSucceededIntegrationEvent)}")
                         .AddWithCustomName<OrderStartedIntegrationEvent>($"{eventSufix}{nameof(OrderStartedIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToAwaitingValidationIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToCancelledIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToCancelledIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToShippedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToSubmittedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToSubmittedIntegrationEvent)}")
                         .AddWithCustomName<OrderStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStockConfirmedIntegrationEvent)}")
                         .AddWithCustomName<OrderStockRejectedIntegrationEvent>($"{eventSufix}{nameof(OrderStockRejectedIntegrationEvent)}")
                         .AddWithCustomName<UserCheckoutAcceptedIntegrationEvent>($"{eventSufix}{nameof(UserCheckoutAcceptedIntegrationEvent)}"))
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
                await bus.Subscribe<GracePeriodConfirmedIntegrationEvent>();
                await bus.Subscribe<OrderPaymentFailedIntegrationEvent>();
                await bus.Subscribe<OrderPaymentSucceededIntegrationEvent>();
                await bus.Subscribe<OrderStockConfirmedIntegrationEvent>();
                await bus.Subscribe<OrderStockRejectedIntegrationEvent>();
                await bus.Subscribe<UserCheckoutAcceptedIntegrationEvent>();
            });
        }

        return services;
    }

    public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // prevent from mapping "sub" claim to nameidentifier.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

        var identityUrl = configuration.GetValue<string>("IdentityUrl");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;

        }).AddJwtBearer(options =>
        {
            options.Authority = identityUrl;
            options.RequireHttpsMetadata = false;
            options.Audience = "orders";
        });

        return services;
    }
}