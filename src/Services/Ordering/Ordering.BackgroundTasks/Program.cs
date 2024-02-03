using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Ordering.BackgroundTasks.Extensions;
using Serilog;
using System;
using System.IO;

namespace Ordering.BackgroundTasks
{
    public class Program
    {
        public static readonly string AppName = typeof(Program).Assembly.GetName().Name;

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Run();
        }

        public static IHost CreateHostBuilder(string[] args)
        {
            var configuration = GetConfiguration();

            return Host.CreateDefaultBuilder(args)
               .AddAwsSecrets(configuration)
               .UseServiceProviderFactory(new AutofacServiceProviderFactory())
               .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>())
               .ConfigureAppConfiguration(x => x.AddConfiguration(configuration))
               .ConfigureLogging((host, builder) => builder.UseSerilog(host.Configuration).AddSerilog())
               .Build();
        }
           
        private static IConfiguration GetConfiguration()
        {
            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            return builder.Build();
        }
    }
}
