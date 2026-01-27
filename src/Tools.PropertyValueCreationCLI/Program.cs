using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestSharp;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Tools.PropertyValueCreationCLI
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("loggerSettings.json", optional: false, reloadOnChange: true)
                .Build();

            var registry = new ConsoleRegistry(configuration);
            registry.For<IConfiguration>().Use(configuration);

            var container = new Container(registry);
            var app = container.GetInstance<Application>();
            app.CheckFile(args[0]);
            app.Run(args[0]);
        }

        public class ConsoleRegistry : ServiceRegistry
        {
            public ConsoleRegistry(IConfigurationRoot config)
            {
                Scan(scan =>
                {
                    scan.TheCallingAssembly();
                    scan.WithDefaultConventions();
                });

                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(config)
                    .CreateLogger();

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().AddSerilog(Log.Logger));
                For<ILoggerFactory>().Use(_ => loggerFactory);
                For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("PropertyValueCreationCLI"));
                For(typeof(ILogger<>)).Use(typeof(Logger<>));

                For<DorcOAuthClientConfiguration>().Use(ctx => new DorcOAuthClientConfiguration(config));
                For<ApiCaller>().Use(ctx => new ApiCaller(ctx.GetInstance<DorcOAuthClientConfiguration>()));
                For<Application>().Use<Application>();

            }
        }

        public class Application
        {
            private readonly ILogger _log;
            private readonly IApiCaller _api;

            public Application(IApiCaller api, ILogger<Application> log)
            {
                _log = log;
                _api = api;
            }

            public void CheckFile(string filePath)
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException("Can not find file" + filePath);

                var configs =
                    new DeployCsvFileReader().GetValues(
                        File.ReadAllLines(filePath));

                var environments = configs
                    .Where(r => !string.IsNullOrEmpty(r.Environment))
                    .Select(r => r.Environment)
                    .Distinct()
                    .ToList();

                foreach (var env in environments)
                {
                    var segments = new Dictionary<string, string> { { "env", env } };

                    var result = _api.Call<List<EnvironmentApiModel>>(
                        Endpoints.RefDataEnvironments,
                        Method.Get,
                        segments
                    );

                    if (!result.IsModelValid)
                    {
                        throw new Exception(
                            $"Failed to check environment '{env}': {result.ErrorMessage}");
                    }

                    if (result.Value == null || !result.Value.Any())
                    {
                        throw new MissingFieldException(
                            "Environment " + env + " doesn't exist in DORC database, please fix in the spreadsheet and retry");
                    }
                }

                foreach (var row in configs)
                {
                    if (row.PropertyName.Length > 64)
                    {
                        throw new OverflowException(
                            $"{row.PropertyName} exceeds the 64 character limit, please fix in the spreadsheet and retry");
                    }
                }
            }
            public void Run(string filepath)
            {
                var configs = new DeployCsvFileReader().GetValues(File.ReadAllLines(filepath));

                var propertiesToCreate = configs
                    .Select(row => new PropertyApiModel
                    {
                        Name = row.PropertyName,
                        Secure = row.IsSecure
                    })
                    .DistinctBy(p => p.Name)
                    .ToList();

                var propertyValuesToCreate = configs
                    .Select(row => new PropertyValueDto
                    {
                        Property = new PropertyApiModel
                        {
                            Name = row.PropertyName,
                            Secure = row.IsSecure
                        },
                        Value = row.Value,
                        PropertyValueFilter = row.Environment
                    })
                    .ToList();

                _log.LogInformation($"Creating {propertiesToCreate.Count} unique properties");
                foreach (var prop in propertiesToCreate)
                {
                    _log.LogInformation($"===== PropertyName: {prop.Name}  Secure: {prop.Secure} =====");
                }

                var propertyBody = JsonSerializer.Serialize(propertiesToCreate);
                var propertyResult = _api.Call<List<Response>>(
                    Endpoints.Properties,
                    Method.Post,
                    body: propertyBody
                );

                if (!propertyResult.IsModelValid)
                {
                    throw new Exception(
                        $"Failed to create properties: {propertyResult.ErrorMessage}");
                }

                if (propertyResult.Value != null)
                {
                    foreach (var result in propertyResult.Value)
                    {
                        if (result.Status != "success")
                        {
                            _log.LogWarning($"Failed to create property: {result.Item} - {result.Status}");
                        }
                    }
                }

                _log.LogInformation($"Creating {propertyValuesToCreate.Count} property values");
                foreach (var pv in propertyValuesToCreate)
                {
                    _log.LogInformation(
                        $"===== PropertyName: {pv.Property?.Name}  Secure: {pv.Property?.Secure}  Value: {pv.Value}  Environment: {pv.PropertyValueFilter ?? "Default"} ====");
                }

                var valueBody = JsonSerializer.Serialize(propertyValuesToCreate);
                var valueResult = _api.Call<List<Response>>(
                    Endpoints.PropertyValues,
                    Method.Post,
                    body: valueBody
                );

                if (!valueResult.IsModelValid)
                {
                    throw new Exception(
                        $"Failed to create property values: {valueResult.ErrorMessage}");
                }

                if (valueResult.Value != null)
                {
                    foreach (var result in valueResult.Value)
                    {
                        if (result.Status != "success")
                        {
                            _log.LogWarning($"Failed to create property value: {result.Item} - {result.Status}");
                        }
                    }
                }
            }
        }
    }
}