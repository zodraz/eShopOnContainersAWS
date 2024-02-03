﻿global using Autofac.Extensions.DependencyInjection;
global using Autofac;
global using Amazon.CloudWatchLogs;
global using Amazon.SecretsManager.Model;
global using Amazon.SimpleNotificationService;
global using Amazon.SQS;
global using Amazon;
global using Microsoft.eShopOnContainers.Services.Catalog.API.IntegrationEvents.EventHandling;
global using Microsoft.eShopOnContainers.Services.Catalog.API.IntegrationEvents.Events;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Extensions;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Infrastructure.ActionResults;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Infrastructure.Exceptions;
global using Microsoft.eShopOnContainers.Services.Catalog.API.IntegrationEvents;
global using Grpc.Core;
global using LogContext = Serilog.Context.LogContext;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Mvc.Filters;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Server.Kestrel.Core;
global using Microsoft.AspNetCore;
global using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
global using Microsoft.Extensions.Logging;
global using Microsoft.EntityFrameworkCore.Design;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Infrastructure;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Infrastructure.EntityConfigurations;
global using Microsoft.eShopOnContainers.Services.Catalog.API;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Model;
global using Microsoft.eShopOnContainers.Services.Catalog.API.ViewModel;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Grpc;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Options;
global using OpenTelemetry;
global using OpenTelemetry.Metrics;
global using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
global using OpenTelemetry.Resources;
global using OpenTelemetry.Trace;
global using Polly.Retry;
global using Polly;
global using Rebus.AwsSnsAndSqs.Config;
global using Rebus.Auditing.Messages;
global using Rebus.Bus;
global using Rebus.Config;
global using Rebus.Extensions.SharedNothing;
global using Rebus.Handlers;
global using Rebus.Config.Outbox;
global using Rebus.OpenTelemetry.Configuration;
global using Rebus.RabbitMq;
global using Rebus.Retry.Simple;
global using Serilog.Enrichers.Span;
global using Serilog.Sinks.AwsCloudWatch;
global using Serilog.Sinks.Grafana.Loki;
global using Serilog;
global using System.Collections.Generic;
global using System.Data.SqlClient;
global using System.Globalization;
global using System.IO.Compression;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Text.RegularExpressions;
global using System.Threading.Tasks;
global using System;
global using Microsoft.eShopOnContainers.Services.Catalog.API.Infrastructure.Filters;
global using HealthChecks.UI.Client;
global using Microsoft.AspNetCore.Diagnostics.HealthChecks;
global using Microsoft.Extensions.Diagnostics.HealthChecks;
global using Microsoft.OpenApi.Models;
global using System.Reflection;
global using static Microsoft.eShopOnContainers.Services.Catalog.API.Extensions.AwsSecretsConfigurationBuilderExtensions;
global using Status = Grpc.Core.Status;
global using StatusCode = Grpc.Core.StatusCode;
