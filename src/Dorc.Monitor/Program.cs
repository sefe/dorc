using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor;
using Dorc.Monitor.Events;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Registry;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.Monitor.Logging;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using System.Reflection;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);

var configurationRoot = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
var monitorConfiguration = new MonitorConfiguration(configurationRoot);

builder.Services.AddTransient(s => configurationRoot);
builder.Services.AddTransient<IMonitorConfiguration>(m => monitorConfiguration);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = monitorConfiguration.ServiceName;
});

#region OpenTelemetry logging initialization
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string executingAssemblyLocation = Assembly.GetExecutingAssembly().Location;
string executingAssemblyDirectoryPath = Path.GetDirectoryName(executingAssemblyLocation)!;
string logFilePath = Path.Combine("c:\\Log\\DOrc\\Deploy\\Services", $"{monitorConfiguration.ServiceName}.log");

// Ensure log directory exists
Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

// Configure logging with OpenTelemetry
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
});

// Add file logging
builder.Logging.AddFile(logFilePath, options =>
{
    options.Append = true;
    options.FileSizeLimitBytes = 10 * 1024 * 1024; // 10MB
    options.MaxRollingFiles = 100;
});

// Add OpenTelemetry with OTLP exporter
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService("Dorc.Monitor", serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"));
    
    // Add OTLP exporter (configure endpoint via environment variables or appsettings)
    var otlpEndpoint = configurationRoot.GetValue<string>("OpenTelemetry:OtlpEndpoint");
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        logging.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
        });
    }
    
    // Console exporter for debugging
    logging.AddConsoleExporter();
});

builder.Logging.SetMinimumLevel(LogLevel.Information);
#endregion

builder.Services.AddTransient<ScriptDispatcher>();

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

