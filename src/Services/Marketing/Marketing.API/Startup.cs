namespace Microsoft.eShopOnContainers.Services.Marketing.API
{
    using Amazon;
    using Amazon.SimpleNotificationService;
    using Amazon.SQS;
    using AspNetCore.Builder;
    using AspNetCore.Hosting;
    using AspNetCore.Http;
    using Autofac;
    using Autofac.Extensions.DependencyInjection;
    using BuildingBlocks.EventBus;
    using EntityFrameworkCore;
    using Extensions.Configuration;
    using Extensions.DependencyInjection;
    using Extensions.Logging;
    using HealthChecks.UI.Client;
    using Infrastructure;
    using Infrastructure.Filters;
    using Infrastructure.Repositories;
    using Infrastructure.Services;
    using IntegrationEvents.Events;
    using Marketing.API.IntegrationEvents.Handlers;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.eShopOnContainers.Services.Marketing.API.Controllers;
    using Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure.Middlewares;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.OpenApi.Models;
    using Prometheus;
    using Rebus.Auditing.Messages;
    using Rebus.AwsSnsAndSqs.Config;
    using Rebus.Config;
    using Rebus.Config.Outbox;
    using Rebus.Extensions.SharedNothing;
    using Rebus.RabbitMq;
    using Rebus.Retry.Simple;
    using System;
    using System.Collections.Generic;
    using System.IdentityModel.Tokens.Jwt;
    using System.Linq;
    using System.Reflection;
    using static Microsoft.eShopOnContainers.Services.Marketing.API.AwsSecretsConfigurationBuilderExtensions;

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
            var marketingSettings = Configuration.Get<MarketingSettings>();
            marketingSettings.AWSOptions = Configuration.GetAWSOptions();
            var eventBusSettings = marketingSettings.EventBus;

            services.AddOpenTelemetry(Configuration, eventBusSettings);
            // Add framework services.
            services.AddCustomHealthCheck(Configuration, eventBusSettings, CurrentEnvironment);
            services
                .AddControllers()
                // Added for functional tests
                .AddApplicationPart(typeof(LocationsController).Assembly)
                .AddNewtonsoftJson()
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.Configure<MarketingSettings>(Configuration);

            ConfigureAuthService(services);

            services.AddEntityFrameworkSqlServer()
                .AddDbContext<MarketingContext>(options =>
            {
                options.UseSqlServer(Configuration["ConnectionString"],
                                     sqlServerOptionsAction: sqlOptions =>
                                     {
                                         sqlOptions.MigrationsAssembly(typeof(Startup).GetTypeInfo().Assembly.GetName().Name);
                                         //Configuring Connection Resiliency: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency 
                                         sqlOptions.EnableRetryOnFailure(maxRetryCount: 15, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                                     });

                // Changing default behavior when client evaluation occurs to throw. 
                // Default in EF Core would be to log a warning when client evaluation is performed.
                //options.ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.QueryClientEvaluationWarning));
                //Check Client vs. Server evaluation: https://docs.microsoft.com/en-us/ef/core/querying/client-eval
            });

            // Add framework services.
            AddCustomSwagger(services);
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder
                    .SetIsOriginAllowed((host) => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
            });

            if (marketingSettings.UseAWS)
            {
                if (marketingSettings.UseDocumentDb)
                {
                    services.AddTransient<IMarketingDataRepository, MarketingDataRepository>();
                }
                else
                {
                    services.AddTransient<IMarketingDataRepository>(x =>
                    {
                        return new DynamoDbMarketingDataRepository(marketingSettings);
                    });
                }
            }
            else
            {
                services.AddTransient<IMarketingDataRepository, MarketingDataRepository>();
            }

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<IIdentityService, IdentityService>();

            services.AddOptions();

            services.AddEventBus(Configuration, eventBusSettings);

            //configure autofac
            var container = new ContainerBuilder();
            container.Populate(services);

            container.RegisterHandlersFromAssemblyOf(typeof(UserLocationUpdatedIntegrationEventHandler));

            return new AutofacServiceProvider(container.Build());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
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
            app.UseCors("CorsPolicy");
            ConfigureAuth(app);

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapDefaultControllerRoute();
                endpoints.MapControllers();
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

            app.UseSwagger()
               .UseSwaggerUI(setup =>
               {
                   setup.SwaggerEndpoint($"{(!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty)}/swagger/v1/swagger.json", "Marketing.API V1");
                   setup.OAuthClientId("marketingswaggerui");
                   setup.OAuthAppName("Marketing Swagger UI");
               });
        }

        private void AddCustomSwagger(IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
             {
                 options.DescribeAllEnumsAsStrings();
                 options.SwaggerDoc("v1", new OpenApiInfo
                 {
                     Title = "eShopOnContainers - Marketing HTTP API",
                     Version = "v1",
                     Description = "The Marketing Service HTTP API"
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
                                { "marketing", "Marketing API" }
                             }
                         }
                     }
                 });

                 options.OperationFilter<AuthorizeCheckOperationFilter>();
             });
        }

        private void ConfigureAuthService(IServiceCollection services)
        {
            // prevent from mapping "sub" claim to nameidentifier.
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(options =>
            {
                options.Authority = Configuration.GetValue<string>("IdentityUrl");
                options.Audience = "marketing";
                options.RequireHttpsMetadata = false;
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
            var localStackUrl = configuration["LocalStack:LocalStackUrl"];
            var useAWS = bool.Parse(configuration["UseAWS"]);
            var useVault = bool.Parse(configuration["UseVault"]);
            var bucketName = configuration.GetValue<string>("S3BucketName");

            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            hcBuilder
                .AddSqlServer(
                    configuration["ConnectionString"],
                    name: "MarketingDB-check",
                    tags: new string[] { "marketingdb" });

            if (useAWS && !useLocalStack)
            {
                //hcBuilder
                //    .AddDynamoDb(options =>
                //    {
                //        options.RegionEndpoint = RegionEndpoint.GetBySystemName(configuration.GetValue<string>("CloudRegion"));
                //    });
            }
            else
            {
                hcBuilder
                    .AddMongoDb(
                        configuration["MongoConnectionString"],
                        name: "MarketingDB-mongodb-check",
                        tags: new string[] { "mongodb" });
            }

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

                    }, name: "marketing-storage-check", tags: new string[] { "marketingstorage" });
            }


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
                        name: "marketing-rabbitmqbus-check",
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
                              options.AddTopicAndSubscriptions($"{eventSufix}{nameof(UserLocationUpdatedIntegrationEvent)}");
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
                             .AddWithCustomName<UserLocationUpdatedIntegrationEvent>($"{eventSufix}{nameof(UserLocationUpdatedIntegrationEvent)}"))
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
                    await bus.Subscribe<UserLocationUpdatedIntegrationEvent>();
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
                            .AddWithCustomName<UserLocationUpdatedIntegrationEvent>($"{eventSufix}{nameof(UserLocationUpdatedIntegrationEvent)}"))
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
                    await bus.Subscribe<UserLocationUpdatedIntegrationEvent>();
                });
            }

            return services;
        }
    }
}
