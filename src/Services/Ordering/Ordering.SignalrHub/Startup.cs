using Microsoft.eShopOnContainers.Services.Ordering.API;

namespace Microsoft.eShopOnContainers.Services.Ordering.SignalrHub;

public class Startup
{
    private readonly IWebHostEnvironment _currentEnvironment;
    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        _currentEnvironment = env;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public IServiceProvider ConfigureServices(IServiceCollection services)
    {
        var orderingSignalrSettings = Configuration.Get<OrderingSignalrSettings>();
        orderingSignalrSettings.AWSOptions = Configuration.GetAWSOptions();
        var eventBusSettings = orderingSignalrSettings.EventBus;

        services.AddOpenTelemetry(Configuration, eventBusSettings);

        services
            .AddCustomHealthCheck(orderingSignalrSettings, _currentEnvironment)
            .AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetIsOriginAllowed((host) => true)
                    .AllowCredentials());
            });

        if (Configuration.GetValue<string>("IsClusterEnv") == bool.TrueString)
        {
            services
                .AddSignalR()
                .AddStackExchangeRedis(Configuration["SignalrStoreConnectionString"]);
        }
        else
        {
            services.AddSignalR();
        }

        ConfigureAuthService(services);

        services.AddEventBus(orderingSignalrSettings);

        services.AddOptions();

        //configure autofac
        var container = new ContainerBuilder();
        container.RegisterModule(new ApplicationModule());
        container.Populate(services);
        container.RegisterHandlersFromAssemblyOf(typeof(OrderStatusChangedToAwaitingValidationIntegrationEventHandler));

        return new AutofacServiceProvider(container.Build());
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
    {
        var pathBase = Configuration["PATH_BASE"];

        if (!string.IsNullOrEmpty(pathBase))
        {
            loggerFactory.CreateLogger<Startup>().LogDebug("Using PATH BASE '{pathBase}'", pathBase);
            app.UsePathBase(pathBase);
        }

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
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {          
            endpoints.MapHub<NotificationsHub>("/hub/notificationhub");
        });
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }

    private void ConfigureAuthService(IServiceCollection services)
    {
        // prevent from mapping "sub" claim to nameidentifier.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

        var identityUrl = Configuration.GetValue<string>("IdentityUrl");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

        }).AddJwtBearer(options =>
        {
            options.Authority = identityUrl;
            options.RequireHttpsMetadata = false;
            options.Audience = "orders.signalrhub";
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];

                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hub/notificationhub")))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });
    }
}

public static class CustomExtensionMethods
{
    private const string eventSufix = "IntegrationEvents-";

    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services,  OrderingSignalrSettings orderingSignalrSettings, 
                                                          IWebHostEnvironment env)
    {
        var awsOptions = orderingSignalrSettings.AWSOptions;
        var useLocalStack = orderingSignalrSettings.LocalStack.UseLocalStack;
        var useAWS = orderingSignalrSettings.UseAWS;
        var useVault = orderingSignalrSettings.UseVault;
        var eventBusSettings = orderingSignalrSettings.EventBus;

        var hcBuilder = services.AddHealthChecks();

        hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

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
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToAwaitingValidationIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToCancelledIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToSubmittedIntegrationEvent)}");
                      });
            }
        }

        if (useVault && useAWS && !useLocalStack)
        {
            var secrets = orderingSignalrSettings.Vault.SecretGroups.Select(grp => $"{env.EnvironmentName}/{grp}").ToList();

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

    public static IServiceCollection AddEventBus(this IServiceCollection services, OrderingSignalrSettings orderingSignalrSettings)
    {
        var eventBusSettings = orderingSignalrSettings.EventBus;

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
                        .AddWithCustomName<OrderStatusChangedToCancelledIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToCancelledIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToShippedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToSubmittedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToSubmittedIntegrationEvent)}"))
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
                    rebusConfig.Outbox(o => o.StoreInSqlServer(orderingSignalrSettings.ConnectionString, "Outbox"));
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
                await bus.Subscribe<OrderStatusChangedToCancelledIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToPaidIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToShippedIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToStockConfirmedIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToSubmittedIntegrationEvent>();
            });
        }
        else
        {
            var useLocalStack = orderingSignalrSettings.LocalStack.UseLocalStack;
            var localStackUrl = orderingSignalrSettings.LocalStack.LocalStackUrl;

            AmazonSQSConfig amazonSqsConfig = new AmazonSQSConfig
            {
                RegionEndpoint = orderingSignalrSettings.AWSOptions.Region
            };

            AmazonSimpleNotificationServiceConfig amazonSimpleNotificationServiceConfig = new AmazonSimpleNotificationServiceConfig
            {
                RegionEndpoint = orderingSignalrSettings.AWSOptions.Region
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
                        .AddWithCustomName<OrderStatusChangedToCancelledIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToCancelledIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToPaidIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToPaidIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToShippedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToShippedIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}")
                        .AddWithCustomName<OrderStatusChangedToSubmittedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToSubmittedIntegrationEvent)}"))
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
                    rebusConfig.Outbox(o => o.StoreInSqlServer(orderingSignalrSettings.ConnectionString, "Outbox"));
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
                await bus.Subscribe<OrderStatusChangedToCancelledIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToPaidIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToShippedIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToStockConfirmedIntegrationEvent>();
                await bus.Subscribe<OrderStatusChangedToSubmittedIntegrationEvent>();
            });
        }

        return services;
    }
}
