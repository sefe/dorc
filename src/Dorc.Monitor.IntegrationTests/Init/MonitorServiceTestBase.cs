using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Events;
using Dorc.Core.HighAvailability;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.IntegrationTests.Init;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Registry;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using JasperFx.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Dorc.Monitor.Tests.Init
{
    public class MonitorServiceTestBase
    {
        private const string logRepo = "test";
        private static readonly object SharedDatabaseLock = new();
        private static string? sharedDisposableConnectionString;

        protected readonly ServiceCollection collection;
        protected ServiceProvider provider;
        private readonly IList<DeploymentRequest> deploymentResultsToCleanup = new List<DeploymentRequest>();

        public MonitorServiceTestBase()
        {
            collection = new ServiceCollection();
            provider = InitializeServiceProvider();
        }

        /// <summary>
        /// Initializes DI and all service types needed for Monitor
        /// </summary>
        /// <returns>Service provider</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal ServiceProvider InitializeServiceProvider()
        {
            collection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            collection.AddTransient<ScriptDispatcher>();

            // Test-only no-op lock service. Post-S-009 the production NoOp class
            // is gone; integration tests still need a non-Kafka stand-in.
            collection.AddSingleton<IDistributedLockService, IntegrationTestNoOpDistributedLockService>();
            collection.AddSingleton<IRequestPollSignal, RequestPollSignal>();

            PersistentSourcesRegistry.Register(collection);

            collection.AddSingleton<IDeploymentNotificationSink, NoOpDeploymentNotificationSink>();
            collection.AddTransient<IDeploymentRequestStateProcessor, DeploymentRequestStateProcessor>();
            collection.AddTransient<IPendingRequestProcessor, PendingRequestProcessor>();
            collection.AddTransient<IVariableScopeOptionsResolver, VariableScopeOptionsResolver>();
            collection.AddTransient<IScriptGroupPipeServer, ScriptGroupPipeServer>();
            collection.AddTransient<ISecurityObjectFilter, SecurityObjectFilter>();
            collection.AddTransient<IRolePrivilegesChecker, RolePrivilegesChecker>();
            collection.AddTransient<IClaimsPrincipalReader, DirectToolClaimsPrincipalReader>();

            var baseConfig = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json")
                .AddEnvironmentVariables()
                .Build();

            var effectiveConnectionString = EnsureTestDatabaseConnectionString(
                baseConfig.GetConnectionString("DOrcConnectionString"));

            var configurationRoot = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json")
                .AddEnvironmentVariables()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DOrcConnectionString"] = effectiveConnectionString,
                    ["AppSettings:ScriptRoot"] = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                    ["AppSettings:RefDataApiUrl"] = "http://localhost:51598"
                })
                .Build();

            var monitorConfiguration = new MonitorConfiguration(configurationRoot);
            collection.AddTransient<IConfiguration>(s => configurationRoot);
            collection.AddTransient(s => configurationRoot);
            collection.AddTransient<IMonitorConfiguration>(m => monitorConfiguration);
            collection.AddTransient<IConfigurationSettings, ConfigurationSettings>();

            collection.AddTransient<IDeploymentContextFactory>(provider => new DeploymentContextFactory(monitorConfiguration.DOrcConnectionString));

            collection.AddSingleton<IDeploymentEventsPublisher>(ctx => Substitute.For<IDeploymentEventsPublisher>());
            collection.AddScoped<IVariableResolver, VariableResolver>();
            collection.AddScoped<IPropertyEvaluator, PropertyEvaluator>();
            collection.AddScoped<IComponentProcessor, ComponentProcessor>();
            collection.AddScoped<IScriptDispatcher, ScriptDispatcher>();
            collection.AddScoped<ITerraformDispatcher>(_ => Substitute.For<ITerraformDispatcher>());
            collection.AddScoped<IAzureStorageAccountWorker>(_ => Substitute.For<IAzureStorageAccountWorker>());
            collection.AddScoped<IGitHubArtifactDownloader>(_ => Substitute.For<IGitHubArtifactDownloader>());
            collection.AddTransient<IDeploymentEngine, DeploymentEngine>();
            collection.AddTransient<IPropertyEncryptor>(serviceProvider =>
            {
                var secureKeyPersistentDataSource = serviceProvider.GetService<ISecureKeyPersistentDataSource>();
                if (secureKeyPersistentDataSource == null)
                {
                    throw new InvalidOperationException("Instance of the interface 'ISecureKeyPersistentDataSource' is not found in the dependency container.");
                }
                return new AesGcmPropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
                    secureKeyPersistentDataSource.GetSymmetricKey());
            });

            collection.AddTransient<MonitorService>();

            return collection.BuildServiceProvider();
        }

        private static string EnsureTestDatabaseConnectionString(string? configuredConnectionString)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnectionString) &&
                !configuredConnectionString.Contains("__DORC_TEST_DB_CONNSTR__", StringComparison.Ordinal))
            {
                var configuredBuilder = new SqlConnectionStringBuilder(configuredConnectionString)
                {
                    Encrypt = SqlConnectionEncryptOption.Mandatory,
                    TrustServerCertificate = true
                };
                if (!string.IsNullOrWhiteSpace(configuredBuilder.InitialCatalog) &&
                    !string.Equals(configuredBuilder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureDatabaseBootstrapped(configuredBuilder.ConnectionString);
                    return configuredBuilder.ConnectionString;
                }
            }

            lock (SharedDatabaseLock)
            {
                if (!string.IsNullOrEmpty(sharedDisposableConnectionString))
                {
                    return sharedDisposableConnectionString;
                }

                var baseConnStr = !string.IsNullOrWhiteSpace(configuredConnectionString) &&
                                  !configuredConnectionString.Contains("__DORC_TEST_DB_CONNSTR__", StringComparison.Ordinal)
                    ? configuredConnectionString
                    : "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;";

                var builder = new SqlConnectionStringBuilder(baseConnStr)
                {
                    InitialCatalog = $"DorcMonitorIntTest_{Guid.NewGuid():N}",
                    Encrypt = SqlConnectionEncryptOption.Mandatory,
                    TrustServerCertificate = true
                };
                sharedDisposableConnectionString = builder.ConnectionString;
                EnsureDatabaseBootstrapped(sharedDisposableConnectionString);

                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        var masterBuilder = new SqlConnectionStringBuilder(sharedDisposableConnectionString)
                        {
                            InitialCatalog = "master",
                            Encrypt = SqlConnectionEncryptOption.Mandatory,
                            TrustServerCertificate = true
                        };
                        using var conn = new SqlConnection(masterBuilder.ConnectionString);
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $@"
IF DB_ID('{builder.InitialCatalog}') IS NOT NULL
BEGIN
    ALTER DATABASE [{builder.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{builder.InitialCatalog}];
END";
                        cmd.ExecuteNonQuery();
                    }
                    catch (SqlException ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"Best-effort test database cleanup failed: {ex.Message}");
                    }
                };

                return sharedDisposableConnectionString;
            }
        }

        private static void EnsureDatabaseBootstrapped(string connectionString)
        {
            var factory = new DeploymentContextFactory(connectionString);
            using var context = (DeploymentContext)factory.GetContext();
            context.Database.EnsureCreated();

            context.Database.ExecuteSqlRaw(@"
IF OBJECT_ID('deploy.DF_Project_ObjectId', 'D') IS NULL
BEGIN
    DECLARE @existingDefault NVARCHAR(128);
    SELECT @existingDefault = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID('deploy.Project') AND c.name = 'ObjectId';

    IF @existingDefault IS NULL
        ALTER TABLE [deploy].[Project] ADD CONSTRAINT [DF_Project_ObjectId] DEFAULT (NEWID()) FOR [ObjectId];
END

IF COL_LENGTH('deploy.Environment', 'Owner') IS NULL
BEGIN
    ALTER TABLE [deploy].[Environment] ADD [Owner] NVARCHAR(100) NULL;
END

ALTER TABLE [deploy].[DeploymentRequest] ALTER COLUMN [RequestDetails] XML NULL;
");

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Dorc.sln")))
            {
                dir = dir.Parent;
            }

            if (dir != null)
            {
                var sqlFiles = new[]
                {
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Functions", "SplitTagString.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "GetFullEnvironmentChain.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "AppendRequestLog.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_global_properties.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_environment_properties.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_by_PropertyName.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_for_user.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "get_property_values_for_user_with_inheritance.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "DuplicateEnvironmentPropertyValues.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "InsertComponent.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "MapProjectToComponent.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "deploy", "Stored Procedures", "MapProjectToEnvironment.sql"),
                    Path.Combine(dir.FullName, "src", "Dorc.Database", "Scripts", "Post-Deployment", "SeedRefDataAuditActions.sql"),
                };

                using var sqlConn = new SqlConnection(connectionString);
                sqlConn.Open();
                foreach (var sqlFile in sqlFiles.Where(File.Exists).Select(path => new { Path = path, Script = File.ReadAllText(path) }))
                {
                    var batches = Regex.Split(sqlFile.Script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    foreach (var cmd in batches.Where(b => !string.IsNullOrWhiteSpace(b)).Select(batch => new SqlCommand(batch, sqlConn)))
                    {
                        using var command = cmd;
                        try
                        {
                            command.ExecuteNonQuery();
                        }
                        catch (SqlException ex) when (ex.Number == 2714) // Object already exists
                        {
                            System.Diagnostics.Trace.WriteLine($"Skipped existing SQL object in {sqlFile.Path}: {ex.Message}");
                        }
                    }
                }
            }

            if (!context.SecureKeys.Any())
            {
                context.SecureKeys.Add(new SecureKey
                {
                    IV = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                    Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                });
            }

            if (!context.PropertyFilters.Any(pf => pf.Name == "Environment"))
            {
                context.PropertyFilters.Add(new PropertyFilter
                {
                    Name = "Environment",
                    Priority = 1
                });
            }

            if (!context.ConfigValues.Any(cv => cv.Key == "ScriptRoot"))
            {
                context.ConfigValues.Add(new ConfigValue
                {
                    Key = "ScriptRoot",
                    Value = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                    Secure = false
                });
            }

            context.SaveChanges();
        }

        /// <summary>
        /// Substitutes any service in DI. Use before GetMonitor to set mocks instead of real services
        /// </summary>
        /// <typeparam name="T">Any type added to DI</typeparam>
        /// <param name="service">object with which type T should be resolved</param>
        protected void SubstituteTransientWith<T>(T service)
            where T : class
        {
            collection.Replace(ServiceDescriptor.Transient<T>(p=> service));
            provider = collection.BuildServiceProvider();
        }

        protected void AddDeploymentRequests(IList<DeploymentRequest> listOfDr)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                context.DeploymentRequests.AddRange(listOfDr);
                context.SaveChanges();

                this.deploymentResultsToCleanup.AddRange(listOfDr);
            }
        }

        protected void DeleteDeploymentRequests(IList<DeploymentRequest> listOfDr)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                context.DeploymentRequests.RemoveRange(listOfDr);
                context.SaveChanges();
            }
        }

        protected int UpdateDeploymentRequestStatus(DeploymentRequest deploymentRequest, DeploymentRequestStatus status)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.RequestedTime, DateTime.UtcNow));

                deploymentRequest.Status = status.ToString();

                return rowsAffected;
            }
        }

        protected DeploymentRequest GetUpdatedDeploymentRequest(DeploymentRequest deploymentRequest)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                return context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id).First();                    
            }
        }

        public void DeleteAllAddedDeploymentRequests()
        {
            DeleteDeploymentRequests(deploymentResultsToCleanup);
        }

        public MonitorService GetMonitorService()
        {
            var monitor = provider.GetService(typeof(MonitorService)) as MonitorService;
            if (monitor == null)
                throw new ApplicationException("monitor was not configured");
            return monitor;
        }
    }
}
