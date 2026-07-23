using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
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
using OpenTelemetry.Metrics;
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

    // Export the Kafka consumer lag/state meter alongside logs. Operators
    // wiring an OTLP collector get consumer lag dashboards for free.
    builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
    {
        metrics.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("Dorc.Monitor", serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.namespace"] = "DOrc",
                ["deployment.environment"] = monitorConfiguration.Environment,
                ["host.name"] = Environment.MachineName
            }));

        metrics.AddMeter(Dorc.Kafka.Client.Observability.KafkaConsumerMetrics.MeterName);
        metrics.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
        });
    });
}
#endregion

builder.Services.AddTransient<ScriptDispatcher>();

// Master Kafka switch. When false, the Monitor runs in single-replica
// DB-poll fallback mode: no Kafka consumers, no distributed lock, no
// Avro/error-log wiring. Default true per the migration intent (this PR
// replaces RabbitMQ with Kafka). Operators upgrading an existing install
// can either ship a Kafka:Enabled=false override or rely on the empty-
// BootstrapServers startup-validation fallback to skip Kafka cleanly.
//
// IMPORTANT: use builder.Configuration (not the dedicated configurationRoot
// above) so environment-variable and CLI overrides for Kafka settings are
// honoured. The dedicated configurationRoot reads only appsettings.json and
// would silently ignore Kubernetes secret / env-var overrides for these keys.
// Upgrade-safety gate shared with the API host (Dorc.Kafka.Client KafkaStartupGate):
// if Kafka is enabled but a required setting is missing, fall back cleanly rather
// than crash at DI resolution. Runs in DB-poll fallback mode when it returns false.
// The fallback report goes through Serilog (configured above), NOT the
// console: under AddWindowsService there is no attached console, so a
// Console.WriteLine here is discarded and operators get no trace of why
// distributed locking vanished.
var kafkaEnabled = Dorc.Kafka.Client.Configuration.KafkaStartupGate.IsKafkaEnabled(
    builder.Configuration, "DB-poll fallback mode",
    message => Log.Warning("{KafkaStartupGateMessage}", message));
// SignalR-client publisher is registered unconditionally as a single
// concrete singleton with two interface forwards. This mirrors the API's
// DirectDeploymentEventPublisher pattern: one instance reachable through
// IDeploymentEventsPublisher (the regular publish path) and through
// IFallbackDeploymentEventPublisher (the dual-publish fallback used by
// KafkaDeploymentEventPublisher when the broker is unreachable).
// When Kafka is enabled, AddDorcKafkaPublisher Replaces only the
// IDeploymentEventsPublisher mapping, leaving the fallback intact.
builder.Services.AddSingleton<SignalRDeploymentEventPublisher>();
builder.Services.AddSingleton<IDeploymentEventsPublisher>(sp =>
    sp.GetRequiredService<SignalRDeploymentEventPublisher>());
builder.Services.AddSingleton<Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher>(sp =>
    sp.GetRequiredService<SignalRDeploymentEventPublisher>());

if (kafkaEnabled)
{
    builder.Services.AddDorcKafkaDistributedLock(builder.Configuration);
    builder.Services.AddDorcKafkaErrorLog(builder.Configuration);
    builder.Services.AddDorcKafkaRequestLifecycleSubstrate(builder.Configuration);
    builder.Services.AddDorcKafkaAvro(builder.Configuration);
    // , the Monitor is the producer of results-status
    // events. Without this registration the new dorc.results.status topic
    // would never be produced to and the API-side projection consumer
    // would subscribe to a permanently empty topic. AddDorcKafkaPublisher
    // Replaces the IDeploymentEventsPublisher registration above with the
    // dual-publish KafkaDeploymentEventPublisher.
    builder.Services.AddDorcKafkaPublisher(builder.Configuration);
}
else
{
    // DB-poll fallback wiring (NoOp lock + in-process poll signal) plus the
    // single-replica split-brain warning — see DbPollFallbackRegistration.
    // Logged at Error through Serilog so it survives Windows-service hosting
    // (Console.WriteLine is discarded without an attached console).
    DbPollFallbackRegistration.Register(builder.Services,
        message => Log.Error("{KafkaFallbackWarning}", message));
}

PersistentSourcesRegistry.Register(builder.Services);

// Transient: DeploymentEngine and DeploymentRequestStateProcessor hold stateful fields
// (_runningTasks, environmentRequestIdRunning, environmentLockBackoff) that must be scoped
// to a single MonitorService hosted-service lifetime. Transient ensures each resolution
// gets a fresh instance, which is correct because MonitorService resolves them once at startup.
// Explicit shutdown timeout matching the Windows SCM ServicesPipeTimeout.
// The host's graceful window is the single controlling timeout for in-flight deployments.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddTransient<Dorc.Monitor.IDeploymentEngine, DeploymentEngine>();
builder.Services.AddTransient<IDeploymentRequestStateProcessor, DeploymentRequestStateProcessor>();

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
builder.Services.AddSingleton<IGitHubHostValidator, GitHubHostValidator>();

builder.Services.AddTransient<GitHubRetryHandler>();
builder.Services.AddHttpClient("GitHubActions", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    client.DefaultRequestHeaders.Add("User-Agent", "DOrc-Monitor");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    client.Timeout = TimeSpan.FromSeconds(300);
}).AddHttpMessageHandler<GitHubRetryHandler>();
builder.Services.AddTransient<IGitHubArtifactDownloader, GitHubArtifactDownloader>();

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
    return new AesGcmPropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
        secureKeyPersistentDataSource.GetSymmetricKey());
});

builder.Services.AddHostedService<MonitorService>();
builder.Services.AddTransient<IClaimsPrincipalReader, DirectToolClaimsPrincipalReader>();

IHost host = builder.Build();
host.Run();

