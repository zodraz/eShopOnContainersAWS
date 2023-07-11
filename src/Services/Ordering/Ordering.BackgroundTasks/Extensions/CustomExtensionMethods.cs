using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.eShopOnContainers.Services.IntegrationEvents.Events;
using Microsoft.eShopOnContainers.Services.Ordering.API;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using Rebus.Auditing.Messages;
using Rebus.AwsSnsAndSqs.Config;
using Rebus.Config;
using Rebus.Config.Outbox;
using Rebus.Extensions.SharedNothing;
using Rebus.RabbitMq;
using Rebus.Retry.Simple;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Sinks.AwsCloudWatch;
using Serilog.Sinks.Grafana.Loki;
using System;
using System.Collections.Generic;
using System.Linq;
using static Ordering.BackgroundTasks.Extensions.AwsSecretsConfigurationBuilderExtensions;

namespace Ordering.BackgroundTasks.Extensions
{
    public static class CustomExtensionMethods
    {
        private const string eventSufix = "IntegrationEvents-";

        public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services, OrderingBkgSettings orderingBkgSettings, 
                                                              IHostEnvironment env)
        {
            var awsOptions = orderingBkgSettings.AWSOptions;
            var useLocalStack = orderingBkgSettings.LocalStack.UseLocalStack;
            var useAWS = orderingBkgSettings.UseAWS;
            var useVault = orderingBkgSettings.UseVault;
            var eventBusSettings = orderingBkgSettings.EventBus;

            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());

            hcBuilder.AddSqlServer(
                    orderingBkgSettings.ConnectionString,
                    name: "OrderingTaskDB-check",
                    tags: new string[] { "orderingtaskdb" });

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
                          .AddSnsTopicsAndSubscriptions(options =>
                          {
                              options.RegionEndpoint = awsOptions.Region;
                              options.AddTopicAndSubscriptions($"{eventSufix}{nameof(GracePeriodConfirmedIntegrationEvent)}");
                          });
                }
            }

            if (useVault && useAWS && !useLocalStack)
            {
                var secrets = orderingBkgSettings.Vault.SecretGroups.Select(grp => $"{env.EnvironmentName}/{grp}").ToList();

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

        public static IServiceCollection AddEventBus(this IServiceCollection services, OrderingBkgSettings orderingBkgSettings)
        {
            var eventBusSettings = orderingBkgSettings.EventBus;

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
                        .Transport(t => t.UseRabbitMqAsOneWayClient(serverConnectionEndpoints))                     
                        .UseSharedNothingApproach(builder => builder
                             .AddWithCustomName<GracePeriodConfirmedIntegrationEvent>($"{eventSufix}{nameof(GracePeriodConfirmedIntegrationEvent)}"))
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
                        rebusConfig.Outbox(o => o.StoreInSqlServer(orderingBkgSettings.ConnectionString, "Outbox"));
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

                var useLocalStack = orderingBkgSettings.LocalStack.UseLocalStack;
                var localStackUrl = orderingBkgSettings.LocalStack.LocalStackUrl;

                AmazonSQSConfig amazonSqsConfig = new AmazonSQSConfig
                {
                    RegionEndpoint = orderingBkgSettings.AWSOptions.Region
                };

                AmazonSimpleNotificationServiceConfig amazonSimpleNotificationServiceConfig = new AmazonSimpleNotificationServiceConfig
                {
                    RegionEndpoint = orderingBkgSettings.AWSOptions.Region
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
                        .Transport(t => t.UseAmazonSnsAndSqsAsOneWayClient(
                                   amazonSqsConfig: amazonSqsConfig,
                                   amazonSimpleNotificationServiceConfig: amazonSimpleNotificationServiceConfig))
                        .UseSharedNothingApproach(builder => builder
                             .AddWithCustomName<GracePeriodConfirmedIntegrationEvent>($"{eventSufix}{nameof(GracePeriodConfirmedIntegrationEvent)}"))
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
                        rebusConfig.Outbox(o => o.StoreInSqlServer(orderingBkgSettings.ConnectionString, "Outbox"));
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

        public static ILoggingBuilder UseSerilog(this ILoggingBuilder builder, IConfiguration configuration)
        {
            var seqServerUrl = configuration["Serilog:SeqServerUrl"];
            var logstashUrl = configuration["Serilog:LogstashgUrl"];
            var lokiUrl = configuration["Serilog:LokiUrl"];
            var useAWS = bool.Parse(configuration["UseAWS"]);
            var useLocalStack = bool.Parse(configuration["LocalStack:UseLocalStack"]);
            var localStackUrl = configuration["LocalStack:LocalStackUrl"];

            var cfg = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.WithProperty("ApplicationContext", Program.AppName)
                    .Enrich.FromLogContext()
                    .Enrich.WithSpan()
                    .WriteTo.Console()
                    .ReadFrom.Configuration(configuration);

            if (!string.IsNullOrWhiteSpace(lokiUrl))
            {
                cfg.WriteTo.GrafanaLoki(lokiUrl);
            }

            if (!string.IsNullOrWhiteSpace(seqServerUrl))
            {
                cfg.WriteTo.Seq(seqServerUrl);
            }

            if (!string.IsNullOrWhiteSpace(logstashUrl))
            {
                cfg.WriteTo.Seq(logstashUrl);
            }

            if (useAWS)
            {
                var awsOptions = configuration.GetAWSOptions();
                AmazonCloudWatchLogsConfig awsConfig = new AmazonCloudWatchLogsConfig();
                awsConfig.RegionEndpoint = awsOptions.Region;

                if (useLocalStack)
                {
                    awsConfig.ServiceURL = localStackUrl;
                }

                var client = new AmazonCloudWatchLogsClient(awsConfig);

                cfg.WriteTo.AmazonCloudWatch(
                        // The name of the log group to log to
                        logGroup: "/eshop/ordering-background",
                        // A string that our log stream names should be prefixed with. We are just specifying the
                        // start timestamp as the log stream prefix
                        logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                        // The AWS CloudWatch client to use
                        cloudWatchClient: client);
            }

            return builder;
        }
    }
}
