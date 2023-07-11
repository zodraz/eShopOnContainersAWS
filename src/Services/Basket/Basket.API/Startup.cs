namespace Microsoft.eShopOnContainers.Services.Basket.API;
public class Startup
{
    protected readonly IWebHostEnvironment CurrentEnvironment;
    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        CurrentEnvironment = env;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public virtual IServiceProvider ConfigureServices(IServiceCollection services)
    {
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
        });

        services.AddControllers(options =>
        {
            options.Filters.Add(typeof(HttpGlobalExceptionFilter));
            options.Filters.Add(typeof(ValidateModelStateFilter));

        }) // Added for functional tests
        .AddApplicationPart(typeof(BasketController).Assembly)
        .AddJsonOptions(options => options.JsonSerializerOptions.WriteIndented = true);

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "eShopOnContainers - Basket HTTP API",
                Version = "v1",
                Description = "The Basket Service HTTP API"
            });

            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows()
                {
                    Implicit = new OpenApiOAuthFlow()
                    {
                        AuthorizationUrl = new Uri($"{Configuration.GetValue<string>("IdentityUrlExternal")}/connect/authorize"),
                        TokenUrl = new Uri($"{Configuration.GetValue<string>("IdentityUrlExternal")}/connect/token"),
                        Scopes = new Dictionary<string, string>()
                        {
                            { "basket", "Basket API" }
                        }
                    }
                }
            });

            options.OperationFilter<AuthorizeCheckOperationFilter>();
        });

        ConfigureAuthService(services);

        var basketSettings = Configuration.Get<BasketSettings>();
        var eventBusSettings = basketSettings.EventBus;

        services.AddOpenTelemetry(Configuration, eventBusSettings);

        services.AddCustomHealthCheck(Configuration, eventBusSettings,CurrentEnvironment);

        //By connecting here we are making sure that our service
        //cannot start until redis is ready. This might slow down startup,
        //but given that there is a delay on resolving the ip address
        //and then creating the connection it seems reasonable to move
        //that cost to startup instead of having the first request pay the
        //penalty.
        services.AddSingleton(sp =>
        {
            var configuration = ConfigurationOptions.Parse(basketSettings.CacheConnectionString, true);

            return ConnectionMultiplexer.Connect(configuration);
        });

        var container = new ContainerBuilder();

        services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy",
                builder => builder
                .SetIsOriginAllowed((host) => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());
        });

        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IBasketRepository, RedisBasketRepository>();
        services.AddTransient<IIdentityService, IdentityService>();

        services.AddEventBus(Configuration, eventBusSettings);

        services.AddOptions();

        container.Populate(services);

        container.RegisterHandlersFromAssemblyOf(typeof(OrderStartedIntegrationEventHandler));

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

        app.UseSwagger()
            .UseSwaggerUI(setup =>
            {
                setup.SwaggerEndpoint($"{ (!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty) }/swagger/v1/swagger.json", "Basket.API V1");
                setup.OAuthClientId("basketswaggerui");
                setup.OAuthAppName("Basket Swagger UI");
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

        app.UseStaticFiles();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapMetrics();
            endpoints.MapGrpcService<BasketService>();
            endpoints.MapDefaultControllerRoute();
            endpoints.MapControllers();
            endpoints.MapGet("/_proto/", async ctx =>
            {
                ctx.Response.ContentType = "text/plain";
                using var fs = new FileStream(Path.Combine(CurrentEnvironment.ContentRootPath, "Proto", "basket.proto"), FileMode.Open, FileAccess.Read);
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
            options.Audience = "basket";
        });
    }

    protected virtual void ConfigureAuth(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
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

        hcBuilder
            .AddRedis(
                configuration["CacheConnectionString"],
                name: "redis-check",
                tags: new string[] { "redis" });

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
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(ProductPriceChangedIntegrationEvent)}");
                          options.AddTopicAndSubscriptions($"{eventSufix}{nameof(OrderStartedIntegrationEvent)}");
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
                         .AddWithCustomName<ProductPriceChangedIntegrationEvent>($"{eventSufix}{nameof(ProductPriceChangedIntegrationEvent)}")
                         .AddWithCustomName<OrderStartedIntegrationEvent>($"{eventSufix}{nameof(OrderStartedIntegrationEvent)}")
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
                await bus.Subscribe<OrderStartedIntegrationEvent>();
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
                         .AddWithCustomName<ProductPriceChangedIntegrationEvent>($"{eventSufix}{nameof(ProductPriceChangedIntegrationEvent)}")
                         .AddWithCustomName<OrderStartedIntegrationEvent>($"{eventSufix}{nameof(OrderStartedIntegrationEvent)}")
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
                await bus.Subscribe<OrderStartedIntegrationEvent>();
                await bus.Subscribe<ProductPriceChangedIntegrationEvent>();
            });
        }

        return services;
    }
}
    