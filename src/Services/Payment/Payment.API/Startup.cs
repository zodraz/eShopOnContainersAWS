namespace Microsoft.eShopOnContainers.Services.Payment.API;

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
    public IServiceProvider ConfigureServices(IServiceCollection services)
    {
        var paymentSettings = Configuration.Get<PaymentSettings>();
        paymentSettings.AWSOptions = Configuration.GetAWSOptions();
        var eventBusSettings = paymentSettings.EventBus;

        services.AddOpenTelemetry(Configuration, eventBusSettings);

        services.AddCustomHealthCheck(Configuration, eventBusSettings, _currentEnvironment);
        services.Configure<PaymentSettings>(Configuration);

        services.AddEventBus(Configuration, eventBusSettings);

        var container = new ContainerBuilder();
        container.Populate(services);
        container.RegisterHandlersFromAssemblyOf(typeof(OrderStatusChangedToStockConfirmedIntegrationEventHandler));

        return new AutofacServiceProvider(container.Build());
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
    {
        var pathBase = Configuration["PATH_BASE"];
        if (!string.IsNullOrEmpty(pathBase))
        {
            app.UsePathBase(pathBase);
        }

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

public static class CustomExtensionMethods
{
    private const string eventSufix = "IntegrationEvents-";

    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services, IConfiguration configuration,
        EventBusSettings eventBusSettings, IWebHostEnvironment env)
    {
        var awsOptions = configuration.GetAWSOptions();
        var useLocalStack = bool.Parse(configuration["LocalStack:UseLocalStack"]);
        var useAWS = bool.Parse(configuration["UseAWS"]);
        var useVault = bool.Parse(configuration["UseVault"]);

        var hcBuilder = services.AddHealthChecks();

        hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

        if (eventBusSettings.RabbitMQEnabled)
        {
            var protocol = eventBusSettings.IsSecure ? "amqps" : "amqp";
            var credentials = string.Empty;

            if (!string.IsNullOrEmpty(eventBusSettings.UserName) && !string.IsNullOrEmpty(eventBusSettings.Password))
            {
                credentials = $"{eventBusSettings.UserName}:{eventBusSettings.Password}@";
            }

            hcBuilder
                .AddRabbitMQ(
                    $"{protocol}://{credentials}{eventBusSettings.Host}",
                    name: "payment-rabbitmqbus-check",
                    tags: new string[] { "rabbitmqbus" });
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
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderPaymentFailedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderPaymentSucceededIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}");
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
                         .AddWithCustomName<OrderPaymentFailedIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentFailedIntegrationEvent)}")
                         .AddWithCustomName<OrderPaymentSucceededIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentSucceededIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}"))
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
                await bus.Subscribe<OrderStatusChangedToStockConfirmedIntegrationEvent>();
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
                         .AddWithCustomName<OrderPaymentFailedIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentFailedIntegrationEvent)}")
                         .AddWithCustomName<OrderPaymentSucceededIntegrationEvent>($"{eventSufix}{nameof(OrderPaymentSucceededIntegrationEvent)}")
                         .AddWithCustomName<OrderStatusChangedToStockConfirmedIntegrationEvent>($"{eventSufix}{nameof(OrderStatusChangedToStockConfirmedIntegrationEvent)}"))
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
                await bus.Subscribe<OrderStatusChangedToStockConfirmedIntegrationEvent>();
            });
        }

        return services;
    }
}
