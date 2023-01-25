using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using HealthChecks.UI.Client;
using Locations.API;
using Locations.API.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure;
using Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure.Filters;
using Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure.Middlewares;
using Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure.Repositories;
using Microsoft.eShopOnContainers.Services.Locations.API.Infrastructure.Services;
using Microsoft.eShopOnContainers.Services.Locations.API.IntegrationEvents.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
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
using static Microsoft.eShopOnContainers.Services.Locations.API.AwsSecretsConfigurationBuilderExtensions;

namespace Microsoft.eShopOnContainers.Services.Locations.API
{
    public class Startup
    {
        protected readonly IWebHostEnvironment CurrentEnvironment;
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            CurrentEnvironment = env;
        }

        public IConfiguration Configuration { get; }

        public virtual IServiceProvider ConfigureServices(IServiceCollection services)
        {
            var locationSettings = Configuration.Get<LocationSettings>();
            locationSettings.AWSOptions = Configuration.GetAWSOptions();
            var eventBusSettings = locationSettings.EventBus;

            services.AddOpenTelemetry(locationSettings);

            services.AddCustomHealthCheck(locationSettings, CurrentEnvironment);

            services.AddControllers(options =>
            {
                options.Filters.Add(typeof(HttpGlobalExceptionFilter));
            })
                // Added for functional tests
                .AddApplicationPart(typeof(LocationsController).Assembly)
                .AddNewtonsoftJson();

            ConfigureAuthService(services);

            // Add framework services.
            services.AddSwaggerGen(options =>
            {
                options.DescribeAllEnumsAsStrings();
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "eShopOnContainers - Location HTTP API",
                    Version = "v1",
                    Description = "The Location Microservice HTTP API. This is a Data-Driven/CRUD microservice sample",
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
                                { "locations", "Locations API" }
                            }
                        }
                    }
                });

                options.OperationFilter<AuthorizeCheckOperationFilter>();

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

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<IIdentityService, IdentityService>();
            services.AddTransient<ILocationsService, LocationsService>();

            if (locationSettings.UseAWS)
            {
                if (locationSettings.UseDocumentDb)
                {
                    services.AddTransient<ILocationsRepository, LocationsRepository>();
                }
                else
                {
                    services.AddTransient<ILocationsRepository>(x =>
                    {
                        return new DynamoDbLocationsRepository(locationSettings);
                    });
                }
            }
            else
            {
                services.AddTransient<ILocationsRepository, LocationsRepository>();
            }

            services.AddEventBus(locationSettings);

            //configure autofac
            var container = new ContainerBuilder();
            container.Populate(services);

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
              .UseSwaggerUI(c =>
              {
                  c.SwaggerEndpoint($"{(!string.IsNullOrEmpty(pathBase) ? pathBase : string.Empty)}/swagger/v1/swagger.json", "Locations.API V1");
                  c.OAuthClientId("locationsswaggerui");
                  c.OAuthAppName("Locations Swagger UI");
              });

            var locationSettings = Configuration.Get<LocationSettings>();
            locationSettings.AWSOptions = Configuration.GetAWSOptions();

            if (locationSettings.UseAWS)
            {
                DynamoDbLocationsContextSeed.SeedAsync(locationSettings, loggerFactory).Wait();
            }
            else
            {
                LocationsContextSeed.SeedAsync(app, loggerFactory).Wait();
            }
        }

        private void ConfigureAuthService(IServiceCollection services)
        {
            // prevent from mapping "sub" claim to nameidentifier.
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = Configuration.GetValue<string>("IdentityUrl");
                options.Audience = "locations";
                options.RequireHttpsMetadata = false;
            });
        }

        protected virtual void ConfigureAuth(IApplicationBuilder app)
        {
            if (Configuration.GetValue<bool>("UseLoadTest"))
            {
                app.UseMiddleware<ByPassAuthMiddleware>();
            }

            app.UseAuthentication();
            app.UseAuthorization();
        }
    }

    public static class CustomExtensionMethods
    {
        private const string eventSufix = "IntegrationEvents-";

        public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services,
             LocationSettings locationSettings, IWebHostEnvironment env)
        {
            var awsOptions = locationSettings.AWSOptions;
            var useLocalStack = locationSettings.LocalStack.UseLocalStack;
            var useAWS = locationSettings.UseAWS;
            var useVault = locationSettings.UseVault;

            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            if (useAWS && !useLocalStack)
            {
                //hcBuilder
                //   .AddDynamoDb(options =>
                //   {
                //       options.RegionEndpoint = awsOptions.Region;
                //   });
            }
            else
            {
                hcBuilder
                    .AddMongoDb(
                       locationSettings.ConnectionString,
                        name: "locations-mongodb-check",
                        tags: new string[] { "mongodb" });
            }

            var eventBusSettings = locationSettings.EventBus;

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
                        name: "locations-rabbitmqbus-check",
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
                var secrets = locationSettings.Vault.SecretGroups.Select(grp => $"{env.EnvironmentName}/{grp}").ToList();

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

        public static IServiceCollection AddEventBus(this IServiceCollection services,LocationSettings locationSettings)
        {
            var eventBusSettings = locationSettings.EventBus;

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
                        rebusConfig.Outbox(o => o.StoreInSqlServer(locationSettings.ConnectionString, "Outbox"));
                    }
                    if (eventBusSettings.AuditEnabled)
                    {
                        rebusConfig.Options(t => t.EnableMessageAuditing(auditQueue: "Audit"));
                    }

                    return rebusConfig;
                });
            }
            else
            {
                AmazonSQSConfig amazonSqsConfig = new AmazonSQSConfig
                {
                    RegionEndpoint = locationSettings.AWSOptions.Region,
                };

                AmazonSimpleNotificationServiceConfig amazonSimpleNotificationServiceConfig = new AmazonSimpleNotificationServiceConfig
                {
                    RegionEndpoint = locationSettings.AWSOptions.Region,
                };

                if (locationSettings.LocalStack.UseLocalStack)
                {
                    amazonSqsConfig.ServiceURL = locationSettings.LocalStack.LocalStackUrl;
                    amazonSimpleNotificationServiceConfig.ServiceURL = locationSettings.LocalStack.LocalStackUrl;
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
                        rebusConfig.Outbox(o => o.StoreInSqlServer(locationSettings.ConnectionString, "Outbox"));
                    }
                    if (eventBusSettings.AuditEnabled)
                    {
                        rebusConfig.Options(t => t.EnableMessageAuditing(auditQueue: "Audit"));
                    }

                    return rebusConfig;
                });
            }

            return services;
        }
    }
}
