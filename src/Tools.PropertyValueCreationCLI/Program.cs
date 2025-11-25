using Dorc.Core;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
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
            registry.IncludeRegistry<PersistentDataRegistry>();

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
                // requires explicit registration; doesn't follow convention
                For<IPropertyValueFilterCreation>().Use<PropertyValueFilterCreation>();
                For<IPropertyCreation>().Use<PropertyCreation>();
                For<IClaimsPrincipalReader>().Use<DirectToolClaimsPrincipalReader>();                

                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(config)
                    .CreateLogger();

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().AddSerilog(Log.Logger));
                For<ILoggerFactory>().Use(_ => loggerFactory);
                For<ILogger>().Use(ctx => ctx.GetInstance<ILoggerFactory>().CreateLogger("PropertyValueCreationCLI"));
                For(typeof(ILogger<>)).Use(typeof(Logger<>));

                For<IPropertyEncryptor>().Use(x => { 
                    var secureKeyPersistentDataSource = x.GetInstance<ISecureKeyPersistentDataSource>();
                    return new PropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
                        secureKeyPersistentDataSource.GetSymmetricKey());
                });
                For<IRolePrivilegesChecker>().Use<RolePrivilegesChecker>();
                For<Application>().Use<Application>();

            }
        }

        public class Application
        {
            private readonly ILogger _log;
            private readonly IPropertyCreation _propertyCreation;
            private readonly IPropertyValueFilterCreation _propertyValueFilterCreation;
            private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

            public Application(IPropertyCreation propertyCreation,
                IPropertyValueFilterCreation propertyValueFilterCreation, ILogger<Application> log, IEnvironmentsPersistentSource environmentsPersistentSource
                )
            {
                _environmentsPersistentSource = environmentsPersistentSource;
                _propertyCreation = propertyCreation;
                _propertyValueFilterCreation = propertyValueFilterCreation;
                _log = log;
            }

            public void CheckFile(string filePath)
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException("Can not find file" + filePath);

                var configs =
                    new DeployCsvFileReader().GetValues(
                        File.ReadAllLines(filePath));

                foreach (var row in configs)
                {
                    if (row.PropertyName.Length > 64)
                        throw new OverflowException(row.PropertyName +
                                                    " exceeds the 64 character limit, please fix in the spreadsheet and retry");
                    {
                        if (!string.IsNullOrEmpty(row.Environment) && !_environmentsPersistentSource.EnvironmentExists(row.Environment)
                            )
                            throw new MissingFieldException(
                                row.Environment +
                                " Environment doesn't exist in DORC database, please fix in the spreadsheet and retry");
                    }
                }
            }

            public void Run(string filepath)
            {
                var configs = new DeployCsvFileReader().GetValues(File.ReadAllLines(filepath));
                foreach (var row in configs)
                {
                    _log.LogInformation("===== PropertyName:" + row.PropertyName + "  Secure:" + row.IsSecure + "  Value:" +
                              row.Value + "  Environment:" + row.Environment + " ====");
                    _propertyCreation.InsertProperty(row.PropertyName, row.IsSecure, Environment.UserName);
                    _propertyValueFilterCreation.InsertPropertyValueFilter(row.PropertyName, row.Value,
                        row.Environment); //do a check that environment exists!
                }
            }
        }
    }
}