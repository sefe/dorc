using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
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
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile("loggerSettings.json", optional: false, reloadOnChange: true)
                    .Build();

                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .CreateLogger();

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().AddSerilog(Log.Logger));
                var logger = loggerFactory.CreateLogger("PropertyValueCreationCLI");

                var api = new ApiCaller(new DorcOAuthClientConfiguration(configuration));
                var app = new Application(api, logger);
                app.CheckFile(args[0]);
                app.Run(args[0]);
            }
            catch (InvalidOperationException configEx) when (configEx.Message.Contains("not configured"))
            {
                Console.WriteLine(DateTime.Now + " - Configuration Error: " + configEx.Message);
                Console.WriteLine(DateTime.Now + " - appsettings error");
            }
            catch (Exception e)
            {
                Console.WriteLine(DateTime.Now + " - Error: " + e.Message);
                if (e.InnerException != null)
                {
                    Console.WriteLine(DateTime.Now + " - Inner Error: " + e.InnerException.Message);
                }
            }
        }

        public class Application
        {
            private readonly ILogger _log;
            private readonly IApiCaller _api;

            public Application(IApiCaller api, ILogger log)
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
                        $"===== PropertyName: {pv.Property?.Name}  Secure: {pv.Property?.Secure}  Value: {(pv.Property?.Secure == true ? "****" : pv.Value)}  Environment: {pv.PropertyValueFilter ?? "Default"} ====");
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