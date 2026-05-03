using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor;
using Dorc.Monitor.Events;
using Dorc.Core.HighAvailability;
using Dorc.Kafka.ErrorLog.DependencyInjection;
using Dorc.Kafka.Events.DependencyInjection;
using Dorc.Kafka.Lock.DependencyInjection;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Registry;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using System.Reflection;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);

var configurationRoot = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("loggerSettings.json", optional: false, reloadOnChange: true)
    .Build();
var monitorConfiguration = new MonitorConfiguration(configurationRoot);

builder.Services.AddTransient(s => configurationRoot);
builder.Services.AddTransient<IMonitorConfiguration>(m => monitorConfiguration);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = monitorConfiguration.ServiceName;
});

#region Logging Configuration
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
});

var environmentSuffix = monitorConfiguration.IsProduction ? "Prod" : "NonProd";
var logConfigPath = "Serilog:WriteTo:0:Args:path";
configurationRoot[logConfigPath] = configurationRoot[logConfigPath]?.Replace("{env}", environmentSuffix);

Log.Logger = new LoggerConfiguration()
    .Enrich.WithThreadId()
    .ReadFrom.Configuration(configurationRoot)
    .CreateLogger();
builder.Logging.AddSerilog(Log.Logger);

var otlpEndpoint = configurationRoot.GetValue<string>("OpenTelemetry:OtlpEndpoint");
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("Dorc.Monitor", serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.namespace"] = "DOrc",
                ["deployment.environment"] = monitorConfiguration.Environment,
                ["host.name"] = Environment.MachineName
            }));

        logging.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
        });
    });
}
#endregion

builder.Services.AddTransient<ScriptDispatcher>();

// Master Kafka switch. When false, the Monitor runs in single-replica
// DB-poll fallback mode: no Kafka consumers, no distributed lock, no
// Avro/error-log wiring. Default false: an existing installation that
// upgrades without configuring BootstrapServers must remain functional.
// Operators opt in by setting Kafka:Enabled=true once brokers are wired.
var kafkaEnabled = configurationRoot.GetValue("Kafka:Enabled", false);
if (kafkaEnabled)
{
    builder.Services.AddDorcKafkaDistributedLock(configurationRoot);
    builder.Services.AddDorcKafkaErrorLog(configurationRoot);
    builder.Services.AddDorcKafkaRequestLifecycleSubstrate(configurationRoot);
    builder.Services.AddDorcKafkaAvro(configurationRoot);
    // Per SPEC-S-007 R-1, the Monitor is the producer of results-status
    // events. Without this registration the new dorc.results.status topic
    // would never be produced to and the API-side projection consumer
    // would subscribe to a permanently empty topic. The dual-publish
    // KafkaDeploymentEventPublisher uses SignalRDeploymentEventPublisher
    // as its IFallbackDeploymentEventPublisher so the SignalR-direct UI
    // path still fires on Kafka outages.
    builder.Services.AddSingleton<SignalRDeploymentEventPublisher>();
    builder.Services.AddSingleton<Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher>(
        sp => sp.GetRequiredService<SignalRDeploymentEventPublisher>());
    builder.Services.AddDorcKafkaPublisher(configurationRoot);
}
else
{
    builder.Services.AddSingleton<IDistributedLockService, NoOpDistributedLockService>();
    builder.Services.AddSingleton<Dorc.Core.Events.IRequestPollSignal, Dorc.Core.Events.RequestPollSignal>();
}

PersistentSourcesRegistry.Register(builder.Services);

// Transient: DeploymentEngine and DeploymentRequestStateProcessor hold stateful fields
// (_runningTasks, environmentRequestIdRunning, environmentLockBackoff) that must be scoped
// to a single MonitorService hosted-service lifetime. Transient ensures each resolution
// gets a fresh instance, which is correct because MonitorService resolves them once at startup.
// Explicit shutdown timeout matching the Windows SCM ServicesPipeTimeout (S-003).
// The host's graceful window is the single controlling timeout for in-flight deployments.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddTransient<Dorc.Monitor.IDeploymentEngine, DeploymentEngine>();
builder.Services.AddTransient<IDeploymentRequestStateProcessor, DeploymentRequestStateProcessor>();

// When Kafka is enabled the Kafka publisher (registered above) wins via
// services.Replace; otherwise SignalR-direct is the only IDeploymentEventsPublisher.
if (!kafkaEnabled)
{
    builder.Services.AddSingleton<IDeploymentEventsPublisher, SignalRDeploymentEventPublisher>();
}
builder.Services.AddTransient<IPendingRequestProcessor, PendingRequestProcessor>();
builder.Services.AddTransient<IVariableScopeOptionsResolver, VariableScopeOptionsResolver>();

#if DEBUG
builder.Services.AddTransient<IScriptGroupPipeServer, ScriptGroupFileWriter>();
#else
    builder.Services.AddTransient<IScriptGroupPipeServer, ScriptGroupPipeServer>();
#endif


builder.Services.AddTransient<ISecurityObjectFilter, SecurityObjectFilter>();
builder.Services.AddTransient<IRolePrivilegesChecker, RolePrivilegesChecker>();

builder.Services.AddTransient<IVariableResolver, VariableResolver>();
builder.Services.AddTransient<IPropertyEvaluator, PropertyEvaluator>();
builder.Services.AddTransient<IComponentProcessor, ComponentProcessor>();
builder.Services.AddTransient<IScriptDispatcher, ScriptDispatcher>();
builder.Services.AddTransient<ITerraformDispatcher, TerraformDispatcher>();
builder.Services.AddTransient<IAzureStorageAccountWorker, AzureStorageAccountWorker>();

builder.Services.AddTransient<IConfigurationSettings, ConfigurationSettings>();

var connectionString = monitorConfiguration.DOrcConnectionString;

builder.Services.AddTransient<IDeploymentContextFactory>(provider => new DeploymentContextFactory(connectionString));
builder.Services.AddDbContext<DeploymentContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(60);
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorNumbersToAdd: null);
    }));


builder.Services.AddTransient<IPropertyEncryptor>(serviceProvider =>
{
    var secureKeyPersistentDataSource = serviceProvider.GetService<ISecureKeyPersistentDataSource>();
    if (secureKeyPersistentDataSource == null)
    {
        throw new InvalidOperationException("Instance of the interface 'ISecureKeyPersistentDataSource' is not found in the dependency container.");
    }
    return new QuantumResistantPropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
        secureKeyPersistentDataSource.GetSymmetricKey());
});

builder.Services.AddHostedService<MonitorService>();
builder.Services.AddTransient<IClaimsPrincipalReader, DirectToolClaimsPrincipalReader>();

IHost host = builder.Build();
host.Run();

