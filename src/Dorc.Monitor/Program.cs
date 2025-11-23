using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor;
using Dorc.Monitor.Events;
using Dorc.Monitor.HighAvailability;
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
            .AddService("Dorc.Monitor", serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"));
        
        logging.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
        });
    });
}
#endregion

builder.Services.AddTransient<ScriptDispatcher>();

// Add HttpClient for OAuth token acquisition
builder.Services.AddHttpClient();

// Register distributed lock service based on HA configuration
builder.Services.AddSingleton<IDistributedLockService>(sp =>
{
    var config = sp.GetRequiredService<IMonitorConfiguration>();
    var logger = sp.GetRequiredService<ILogger<RabbitMqDistributedLockService>>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    
    if (config.HighAvailabilityEnabled)
    {
        return new RabbitMqDistributedLockService(logger, config, httpClientFactory);
    }
    else
    {
        return new NoOpDistributedLockService();
    }
});

PersistentSourcesRegistry.Register(builder.Services);

builder.Services.AddTransient<Dorc.Monitor.IDeploymentEngine, DeploymentEngine>();
builder.Services.AddTransient<IDeploymentRequestStateProcessor, DeploymentRequestStateProcessor>();

builder.Services.AddSingleton<IDeploymentEventsPublisher, SignalRDeploymentEventPublisher>();
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
builder.Services.AddDbContext<DeploymentContext>(
    options =>
        options.UseSqlServer(connectionString));

builder.Services.AddTransient<IPropertyEncryptor>(serviceProvider =>
{
    var secureKeyPersistentDataSource = serviceProvider.GetService<ISecureKeyPersistentDataSource>();
    if (secureKeyPersistentDataSource == null)
    {
        throw new InvalidOperationException("Instance of the interface 'ISecureKeyPersistentDataSource' is not found in the dependency container.");
    }
    return new PropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
        secureKeyPersistentDataSource.GetSymmetricKey());
});

builder.Services.AddHostedService<MonitorService>();
builder.Services.AddTransient<IClaimsPrincipalReader, DirectToolClaimsPrincipalReader>();

IHost host = builder.Build();
host.Run();

