using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Registry;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using log4net.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

#region log4net logger initialization
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string executingAssemblyLocation = Assembly.GetExecutingAssembly().Location;
string executingAssemblyDirectoryPath = Path.GetDirectoryName(executingAssemblyLocation)!;
string log4netFilePath = Path.Combine(executingAssemblyDirectoryPath, "log4net.config");
FileInfo log4netFileInfo = new FileInfo(log4netFilePath);
XmlConfigurator.Configure(log4netFileInfo);

Type loggerType = MethodBase.GetCurrentMethod()?.DeclaringType!;
var logger = LogManager.GetLogger(loggerType);
#endregion

builder.Services.AddSingleton<ILog>(logger);

builder.Services.AddTransient<ScriptDispatcher>();

PersistentSourcesRegistry.Register(builder.Services);

builder.Services.AddTransient<IDeploymentEngine, DeploymentEngine>();
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

