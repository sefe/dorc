using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;

namespace Dorc.Monitor
{
    public class ScriptDispatcher : IScriptDispatcher
    {
        private string ProdDeployUsernamePropertyName = "DORC_ProdDeployUsername";   
        private string ProdDeployPasswordPropertyName = "DORC_ProdDeployPassword";   
        private string NonProdDeployUsernamePropertyName = "DORC_NonProdDeployUsername";   
        private string NonProdDeployPasswordPropertyName = "DORC_NonProdDeployPassword";   

        private readonly ILogger logger;
        private readonly IDeploymentRequestProcessesPersistentSource processesPersistentSource;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IScriptGroupPipeServer scriptGroupPipeServer;
        private readonly IConfigurationSettings configurationSettingsEngine;
        private readonly IRequestsPersistentSource requestsPersistentSource;

        private bool isScriptExecutionSuccessful; // This field is needed to be instance-wide since Runner process errors are processed as instance-wide events.

        private StringBuilder componentResultLogBuilder = new StringBuilder();

        public ScriptDispatcher(
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ScriptDispatcher> logger,
            IScriptGroupPipeServer scriptGroupPipeServer,
            IConfigurationSettings configurationSettingsEngine,
            IRequestsPersistentSource requestsPersistentSource)
        {
            this.processesPersistentSource = processesPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            this.logger = logger;
            this.scriptGroupPipeServer = scriptGroupPipeServer;
            this.configurationSettingsEngine = configurationSettingsEngine;
            this.requestsPersistentSource = requestsPersistentSource;
        }

        public bool Dispatch(string scriptsLocation,
            ScriptApiModel script,
            IDictionary<string, VariableValue> properties,
            int requestId,
            int deploymentRequestId,
            bool isProduction,
            string environmentName,
            StringBuilder componentResultLogBuilder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            isScriptExecutionSuccessful = true;

            this.componentResultLogBuilder = componentResultLogBuilder;

            var processCredentials = GetProcessCredentials(isProduction, environmentName);
            string processAccountName = processCredentials.Item1;
            string processAccountPassword = processCredentials.Item2;
            if (string.IsNullOrEmpty(processAccountName)
                || string.IsNullOrEmpty(processAccountPassword))
            {
                logger.LogError($"Unable to find a valid DOrc Username or Password for environment '{environmentName}'.");

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            IList<ScriptGroup> scriptGroups = GetScriptsGroupedByPowerShellVersion(
                script,
                scriptsLocation,
                properties,
                deploymentRequestId);

            foreach (ScriptGroup scriptGroup in scriptGroups)
            {
                var domainName = configurationSettingsEngine.GetConfigurationDomainNameIntra();

                //Create named pipe server for each process that is for each script group
                using (var pipeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    string startedScriptGroupPipeName = "DOrcMonitor-" + requestId;
                    Task scriptGroupPipeTask = scriptGroupPipeServer.Start(
                            startedScriptGroupPipeName,
                            scriptGroup,
                            pipeCancellationTokenSource.Token);
                    logger.LogInformation($"Server named pipe with the name '{startedScriptGroupPipeName}' has started.");

                    var contextBuilder = new ProcessSecurityContextBuilder(logger)
                    {
                        UserName = processAccountName,
                        Domain = domainName,
                        Password = processAccountPassword
                    };

                    using (var securityContext = contextBuilder.Build())
                    {
                        var runnerLogPathSetting = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                        .GetSection("AppSettings")["RunnerLogPath"]!;
                        var runnerLogPath = runnerLogPathSetting + $"\\{startedScriptGroupPipeName}.txt";
                        var uncLogPath = runnerLogPath.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));

                        requestsPersistentSource.UpdateUncLogPath(requestId, uncLogPath);

                        var processStarter = new RunnerProcessStarter(logger)
                        {
                            RunnerExecutableFullName = GetDeploymentRunnerFileFullName(
                                scriptGroup.PowerShellVersionNumber),
                            ScriptGroupPipeName = startedScriptGroupPipeName,
                            RunnerLogPath = runnerLogPath
                        };

                        try
                        {
                            Interop.Kernel32.STARTUPINFO startupInfo = new ProcessStartupInfoBuilder(logger).Build();
                            logger.LogInformation("Starting Runner process.");

                            cancellationToken.ThrowIfCancellationRequested();

                            var process = processStarter.Start(startupInfo, securityContext);
                            try
                            {
                                processesPersistentSource.AssociateProcessWithRequest((int)process.Id, requestId);

                                if (cancellationToken.IsCancellationRequested)
                                {
                                    logger.LogDebug("Trying to terminate the Runner process.");
                                    process.Kill();
                                    logger.LogInformation("The Runner process is terminated.");
                                    throw new OperationCanceledException("The Runner process is terminated.");
                                }

                                logger.LogDebug("Waiting for process to exit.");
                                using var killOnCancel = cancellationToken.Register(() =>
                                {
                                    try
                                    {
                                        logger.LogInformation("Cancellation requested — terminating Runner process for request ID '{RequestId}'.", requestId);
                                        process.Kill();
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogDebug(ex, "Failed to terminate Runner process (may have already exited).");
                                    }
                                });
                                var resultCode = process.WaitForExit();
                                logger.LogInformation($"Runner finished for request ID '{requestId}' with result code [{resultCode}]");

                                cancellationToken.ThrowIfCancellationRequested();

                                if (resultCode == RunnerProcess.RunnerProcess.ProcessTerminatedExitCode)
                                {
                                    logger.LogInformation("The Runner process is terminated.");
                                    throw new OperationCanceledException("The Runner process is terminated.");
                                }
                                else if (resultCode != 0)
                                {
                                    isScriptExecutionSuccessful = false;
                                    logger.LogError($"Runner process for request ID '{requestId}' exited with non-zero exit code {resultCode}.");
                                }
                                else
                                {
                                    logger.LogInformation($"Runner process for request ID '{requestId}' completed successfully.");
                                }
                            }
                            finally
                            {
                                pipeCancellationTokenSource.Cancel();
                                processesPersistentSource.RemoveProcess((int)process.Id);
                                process.Dispose();
                            }
                        }
                        catch (Exception e)
                        {
                            pipeCancellationTokenSource.Cancel();
                            logger.LogError($"Exception is thrown while operating with the Runner process. Exception: {e}");
                            throw;
                        }
                    }
                }
            }

            return isScriptExecutionSuccessful;
        }

        private IList<ScriptGroup> GetScriptsGroupedByPowerShellVersion(
            ScriptApiModel script,
            string scriptsLocation,
            IDictionary<string, VariableValue> properties,
            int deploymentResultId)
        {
            IList<ScriptGroup> scriptGroups = new List<ScriptGroup>();

            ScriptGroup? previousScriptGroup = null;
            var scriptApiModel = script;

            {
                if (previousScriptGroup == null
                    || !scriptApiModel.PowerShellVersionNumber.Equals(previousScriptGroup.PowerShellVersionNumber))
                {
                    ScriptGroup scriptGroup = new ScriptGroup()
                    {
                        ID = Guid.NewGuid(),
                        DeployResultId = deploymentResultId,
                        PowerShellVersionNumber = scriptApiModel.PowerShellVersionNumber,
                        ScriptsLocation = scriptsLocation,
                        CommonProperties = properties,
                        ScriptProperties = new List<ScriptProperties>()
                    };

                    scriptGroups.Add(scriptGroup);
                    previousScriptGroup = scriptGroup;
                }

                previousScriptGroup.ScriptProperties.Add(new ScriptProperties()
                {
                    ScriptPath = ExtractPath(scriptsLocation, scriptApiModel),
                    Properties = ExtractProperties(scriptApiModel)
                });
            }

            return scriptGroups;
        }

        private (string, string) GetProcessCredentials(bool isProduction, string environmentName)
        {
            if (isProduction)
            {
                return (GetConfigValue(ProdDeployUsernamePropertyName),
                    GetConfigValue(ProdDeployPasswordPropertyName));
            }

            return (GetConfigValue(NonProdDeployUsernamePropertyName),
                GetConfigValue(NonProdDeployPasswordPropertyName));
        }

        private string GetDeploymentRunnerFileFullName(
            string powerShellVersionNumber)
        {
            string deploymentRunnerPath;

            if (string.IsNullOrEmpty(powerShellVersionNumber)
                || powerShellVersionNumber.Equals(PowerShellVersion.V5_1.ToVersionString()))     
            {
                deploymentRunnerPath = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                .GetSection("AppSettings")["DotNetFrameworkDeploymentRunnerPath"]!;
            }
            else
            {
                deploymentRunnerPath = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                .GetSection("AppSettings")["DotNetCoreDeploymentRunnerPath"]!;
            }

            if (!File.Exists(deploymentRunnerPath))
            {
                logger.LogError($"Runner with the path '{deploymentRunnerPath}' is NOT found.");
                throw new InvalidOperationException("Specified DeploymentRunnerPath does not exist");
            }
            var fileInfo = new FileInfo(deploymentRunnerPath);

            logger.LogInformation($"Runner with the file full name '{fileInfo.FullName}' is chosen.");

            return fileInfo.FullName;
        }

        private string GetConfigValue(string configValue)
        {
            if (string.IsNullOrEmpty(configValue))
                throw new ApplicationException($"Config value name is empty, should have a value"); 
            return _configValuesPersistentSource.GetConfigValue(configValue);
        }


        private string ExtractPath(string scriptsLocation, ScriptApiModel scriptApiModel)
        {
            if (!scriptApiModel.IsPathJSON)
            {
                return Path.Combine(scriptsLocation, scriptApiModel.Path);
            }

            logger.LogInformation($"Found a JSON Path with {scriptApiModel.Path}.");

            var deserializeObject = JsonSerializer.Deserialize<JSONPath>(scriptApiModel.Path);

            if (deserializeObject == null)
            {
                return string.Empty;
            }

            return Path.Combine(scriptsLocation, deserializeObject.ScriptPath);
        }

        private IDictionary<string, VariableValue> ExtractProperties(ScriptApiModel scriptApiModel)
        {
            var properties = new Dictionary<string, VariableValue>();
            if (!scriptApiModel.IsPathJSON)
            {
                return properties;
            }

            var deserializeObject = JsonSerializer.Deserialize<JSONPath>(scriptApiModel.Path);

            if (deserializeObject == null)
            {
                return properties;
            }

            foreach (var deserializeObjectParam in deserializeObject.GenericArguments)
            {
                var first = deserializeObjectParam.Values.First();
                properties.TryAdd(deserializeObjectParam.Keys.First(), new VariableValue { Value = first, Type = first.GetType() });
            }

            return properties;
        }
    }
}
