using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;
using Dorc.Kafka.Events.Publisher;

namespace Dorc.Monitor
{
    public class ScriptDispatcher : IScriptDispatcher
    {
        private readonly ILogger logger;
        private readonly IDeploymentRequestProcessesPersistentSource processesPersistentSource;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IScriptGroupPipeServer scriptGroupPipeServer;
        private readonly IConfigurationSettings configurationSettingsEngine;
        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IScriptScopeConfigValues _scriptScopeConfigValues;
        private readonly IDeploymentCredentialSource _credentialSource;

        private bool isScriptExecutionSuccessful; // This field is needed to be instance-wide since Runner process errors are processed as instance-wide events.

        private StringBuilder componentResultLogBuilder = new StringBuilder();

        public ScriptDispatcher(
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ScriptDispatcher> logger,
            IScriptGroupPipeServer scriptGroupPipeServer,
            IConfigurationSettings configurationSettingsEngine,
            IRequestsPersistentSource requestsPersistentSource,
            IScriptScopeConfigValues scriptScopeConfigValues,
            IDeploymentCredentialSource credentialSource)
        {
            this.processesPersistentSource = processesPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            this.logger = logger;
            this.scriptGroupPipeServer = scriptGroupPipeServer;
            this.configurationSettingsEngine = configurationSettingsEngine;
            this.requestsPersistentSource = requestsPersistentSource;
            this._scriptScopeConfigValues = scriptScopeConfigValues;
            this._credentialSource = credentialSource;
        }

        public bool Dispatch(string scriptsLocation,
            ScriptApiModel script,
            IDictionary<string, VariableValue> properties,
            int requestId,
            int deploymentRequestId,
            bool isProduction,
            string environmentName,
            string? executionIdentityReference,
            StringBuilder componentResultLogBuilder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            isScriptExecutionSuccessful = true;

            this.componentResultLogBuilder = componentResultLogBuilder;

            // One resolution point, shared with the Terraform dispatcher, the daemon status
            // probe and the password reset controller. Four independent copies of the same four
            // key names and the same production boolean is four places for a security decision
            // to drift.
            // Environment-keyed. An environment naming its own execution identity deploys under
            // it; one that does not falls back to the tier default and behaves exactly as before,
            // so migration proceeds environment by environment rather than as a flag day.
            var credential = _credentialSource.Resolve(
                isProduction ? DeploymentTier.Production : DeploymentTier.NonProduction,
                executionIdentityReference);

            if (credential == null)
            {
                logger.LogError(
                    "No deployment credential is available for environment '{EnvironmentName}'."
                    + " Credentials are read from {Source}.",
                    environmentName,
                    _credentialSource.Description);

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            var processAccountName = credential.UserName;
            var processAccountPassword = credential.Password;

            IList<ScriptGroup> scriptGroups = GetScriptsGroupedByPowerShellVersion(
                script,
                scriptsLocation,
                properties,
                deploymentRequestId);

            if (!ScriptPathsAreConfined(script, scriptGroups, scriptsLocation))
            {
                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            foreach (ScriptGroup scriptGroup in scriptGroups)
            {
                var domainName = configurationSettingsEngine.GetConfigurationDomainNameIntra();

                //Create named pipe server for each process that is for each script group
                using (var pipeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    string startedScriptGroupPipeName = $"DOrcMonitor-{HostInstanceId.Value}-{requestId}";

                    // The script group is made available to the account the Runner will be
                    // started as, taken from the same credential resolution that builds the
                    // process security context below rather than assumed to be the Monitor's
                    // own identity.
                    var readerIdentity = new ScriptGroupReaderIdentity(domainName, processAccountName);

                    try
                    {
                        Task scriptGroupPipeTask = scriptGroupPipeServer.Start(
                                startedScriptGroupPipeName,
                                scriptGroup,
                                readerIdentity,
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
                                        // This callback can fire SYNCHRONOUSLY on the Kafka
                                        // rebalance/poll thread: when a distributed lock is lost,
                                        // KafkaLockCoordinator cancels LockLostToken inline, and this
                                        // token is linked to it (DeploymentRequestStateProcessor).
                                        // process.Kill() can block on a slow/zombie child, and blocking
                                        // the poll thread past max.poll.interval.ms fences the lock
                                        // consumer and stalls distributed locking fleet-wide (audit CR#1).
                                        // Offload the blocking termination so the cancellation callback
                                        // returns immediately — honouring the "callbacks on LockLostToken
                                        // must be fast/non-blocking" contract documented in
                                        // KafkaLockCoordinator. Use a DEDICATED thread, not Task.Run: under
                                        // thread-pool saturation a queued Kill could run arbitrarily late,
                                        // leaving a runaway Runner executing deployment work after the
                                        // distributed lock was lost (audit round-2 #6). A dedicated thread
                                        // runs promptly regardless of pool state.
                                        logger.LogInformation("Cancellation requested — terminating Runner process for request ID '{RequestId}'.", requestId);
                                        var killThread = new Thread(() =>
                                        {
                                            try
                                            {
                                                process.Kill();
                                            }
                                            catch (Exception ex)
                                            {
                                                logger.LogDebug(ex, "Failed to terminate Runner process (may have already exited).");
                                            }
                                        })
                                        {
                                            IsBackground = true,
                                            Name = $"runner-kill-{requestId}"
                                        };
                                        killThread.Start();
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
                    finally
                    {
                        // The bundle carries the resolved deployment properties, secrets
                        // included. It is needed for the span of one Runner process, so it
                        // is withdrawn as soon as that process is gone - on the failure and
                        // cancellation paths as much as the successful one.
                        scriptGroupPipeServer.Expire(startedScriptGroupPipeName);
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
                        CommonProperties = WithheldKeysRemoved(properties),
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

        /// <summary>
        /// The second place the withheld keys are excluded, and deliberately not the only one.
        ///
        /// They are already excluded where configuration values become properties. This catches
        /// anything that reached the property set by another route - an environment property or
        /// a request-supplied value bearing the same name - before it is serialised into the
        /// script group and handed to a Runner. The script group is the artefact that crosses
        /// the process boundary, so it is the right place for the invariant to hold rather than
        /// be assumed.
        /// </summary>
        private IDictionary<string, VariableValue> WithheldKeysRemoved(
            IDictionary<string, VariableValue> properties)
        {
            var withheld = properties.Keys.Where(_scriptScopeConfigValues.IsWithheld).ToList();

            if (withheld.Count == 0)
            {
                return properties;
            }

            logger.LogWarning(
                "Removed {Count} withheld key(s) from the script group before dispatch: {Keys}."
                + " These reached the property set by a route other than configuration values.",
                withheld.Count,
                string.Join(", ", withheld));

            return properties
                .Where(property => !_scriptScopeConfigValues.IsWithheld(property.Key))
                .ToDictionary(property => property.Key, property => property.Value);
        }

        /// <summary>
        /// Refuses, or reports, a script path that resolves outside the script root.
        ///
        /// This is the dispatch half of the confinement the write path already applies. Both are
        /// needed for different reasons: the write path stops new components being registered
        /// off-share, and this stops the ones already stored from executing.
        ///
        /// The primary check is on the STORED path, using the same rule the write path uses, so
        /// a validator and its enforcer cannot disagree about what is acceptable. It is also the
        /// only check that answers identically everywhere: Path.Combine is platform-dependent -
        /// it discards the root given a rooted second argument on Windows, and merely
        /// concatenates on other hosts - so a check applied to the JOINED result would pass on a
        /// non-Windows build host and refuse in production, or the reverse. The joined result is
        /// checked too, as a second gate, but nothing rests on it alone.
        ///
        /// Reporting is the default and enforcement is opt-in. Existing components hold paths
        /// that were never validated; enforcing on the day this ships would fail every
        /// deployment of an off-share component at once, which is the flag day the sequencing
        /// rules exist to prevent. The report identifies the population to remediate, and
        /// enforcement is turned on once the estate is clean.
        ///
        /// The path is logged. It names a script location rather than carrying a value, and the
        /// whole point of the report is that an operator can act on it.
        /// </summary>
        private bool ScriptPathsAreConfined(
            ScriptApiModel script, IEnumerable<ScriptGroup> scriptGroups, string scriptsLocation)
        {
            var enforcing = configurationSettingsEngine.GetScriptPathEnforcementEnabled();
            var confined = true;

            if (!ScriptPathConfinement.IsConfined(script.Path, out var reason))
            {
                confined = false;
                Report(enforcing, script.Path, scriptsLocation, reason);
            }

            foreach (var scriptGroup in scriptGroups)
            {
                foreach (var scriptProperties in scriptGroup.ScriptProperties)
                {
                    if (PathConfinement.IsWithin(scriptProperties.ScriptPath, scriptsLocation))
                    {
                        continue;
                    }

                    confined = false;
                    Report(enforcing, scriptProperties.ScriptPath, scriptsLocation,
                        "the resolved path does not lie beneath the script root.");
                }
            }

            // Reporting mode reports and proceeds. Only enforcement stops the deployment.
            return confined || !enforcing;
        }

        private void Report(bool enforcing, string? scriptPath, string scriptsLocation, string reason)
        {
            if (enforcing)
            {
                logger.LogError(
                    "Refusing to execute '{ScriptPath}' against script root '{ScriptRoot}', because"
                    + " {Reason} Register the script beneath the root, or correct the component's"
                    + " stored path.",
                    scriptPath, scriptsLocation, reason);
            }
            else
            {
                logger.LogWarning(
                    "Script path '{ScriptPath}' would be refused once confinement is enforced,"
                    + " because {Reason} Executing it because 'EnforceScriptPathConfinement' is not"
                    + " enabled. Script root: '{ScriptRoot}'.",
                    scriptPath, reason, scriptsLocation);
            }
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
