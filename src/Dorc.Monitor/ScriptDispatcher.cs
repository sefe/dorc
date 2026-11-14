using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Security;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        private readonly IScriptsPersistentSource _scriptsPersistentSource;
        private readonly IPowerShellEvidenceCapture evidenceCapture;

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
            IDeploymentCredentialSource credentialSource,
            IScriptsPersistentSource scriptsPersistentSource,
            IPowerShellEvidenceCapture? evidenceCapture = null)
        {
            this._scriptsPersistentSource = scriptsPersistentSource;
            this.processesPersistentSource = processesPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            this.logger = logger;
            this.scriptGroupPipeServer = scriptGroupPipeServer;
            this.configurationSettingsEngine = configurationSettingsEngine;
            this.requestsPersistentSource = requestsPersistentSource;
            this._scriptScopeConfigValues = scriptScopeConfigValues;
            this._credentialSource = credentialSource;
            this.evidenceCapture = evidenceCapture ?? new PowerShellEvidenceCapture();
        }

        public bool Dispatch(string scriptsLocation,
            ScriptApiModel script,
            IDictionary<string, VariableValue> properties,
            int requestId,
            int deploymentRequestId,
            bool isProduction,
            string environmentName,
            string? executionIdentityReference,
            RequestExecutionIdentityAdoption identityAdoption,
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
            using var credential = identityAdoption.Resolve(
                _credentialSource,
                isProduction ? DeploymentTier.Production : DeploymentTier.NonProduction,
                executionIdentityReference);

            if (credential == null)
            {
                var refusalMessage = RequestExecutionIdentityAdoption.FormatRefusalMessage(
                    environmentName,
                    executionIdentityReference,
                    _credentialSource.Description);
                logger.LogError(
                    "No deployment credential is available for environment '{EnvironmentName}'."
                    + " Credentials are read from {Source}.",
                    LogText.SingleLine(environmentName),
                    _credentialSource.Description);
                componentResultLogBuilder?.AppendLine(refusalMessage);

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            var processAccountName = credential.UserName;

            if (!ScriptPathsAreConfined(script, scriptsLocation, properties, out var alreadyReported))
            {
                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            IList<ScriptGroup> scriptGroups = GetScriptsGroupedByPowerShellVersion(
                script,
                scriptsLocation,
                properties,
                deploymentRequestId);

            if (!ResolvedScriptGroupPathsAreConfined(scriptGroups, scriptsLocation, alreadyReported))
            {
                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            ApplyContentBaseline(script, scriptsLocation, scriptGroups);

            var ordinalOffset = 0;
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
                    scriptGroup.SourceBuildIdentity = requestsPersistentSource.GetRequest(requestId)?.BuildNumber;
                    evidenceCapture.Prepare(scriptGroup, script.Revision, requestId,
                        ordinalOffset, readerIdentity.QualifiedAccountName);
                    ordinalOffset += scriptGroup.ScriptProperties.Count;

                    Exception? dispatchFailure = null;
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
                            Domain = domainName
                        };

                        using (var securityContext = contextBuilder.Build(credential.Password))
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
                                            catch (InvalidOperationException ex)
                                            {
                                                logger.LogDebug(ex, "Failed to terminate Runner process (it has already exited).");
                                            }
                                            catch (System.ComponentModel.Win32Exception ex)
                                            {
                                                logger.LogDebug(ex, "Failed to terminate Runner process (the operating system refused the termination).");
                                            }
                                            catch (NotSupportedException ex)
                                            {
                                                logger.LogDebug(ex, "Failed to terminate Runner process (termination is not supported for it).");
                                            }
                                            catch (SystemException ex)
                                            {
                                                // This runs on a dedicated thread: anything that escapes it
                                                // terminates the Monitor. The three cases above are what
                                                // Process.Kill documents; this is the backstop that keeps an
                                                // undocumented one from taking the service down with it.
                                                logger.LogWarning(ex, "Failed to terminate Runner process.");
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
                    catch (Exception e)
                    {
                        dispatchFailure = e;
                        throw;
                    }
                    finally
                    {
                        // The bundle carries the resolved deployment properties, secrets
                        // included. It is needed for the span of one Runner process, so it
                        // is withdrawn as soon as that process is gone - on the failure and
                        // cancellation paths as much as the successful one.
                        scriptGroupPipeServer.Expire(startedScriptGroupPipeName);
                        // Evidence was allocated before the Runner was launched, so it is
                        // ingested here even when the launch itself failed; otherwise the
                        // restricted directory leaks and the failure leaves no evidence rows.
                        isScriptExecutionSuccessful &= IngestEvidence(scriptGroup, dispatchFailure);
                    }
                }
            }

            return isScriptExecutionSuccessful;
        }

        // An evidence persistence failure must surface when the dispatch itself
        // succeeded, but it must not replace the original failure (notably an
        // OperationCanceledException) when the dispatch had already failed.
        private bool IngestEvidence(ScriptGroup scriptGroup, Exception? dispatchFailure)
        {
            try
            {
                return evidenceCapture.Ingest(scriptGroup, requestsPersistentSource);
            }
            catch (Exception e) when (dispatchFailure != null)
            {
                logger.LogError(e,
                    "PowerShell evidence for result ID '{ResultId}' could not be persisted after the dispatch failed; the restricted evidence directory '{Directory}' is retained.",
                    scriptGroup.DeployResultId, scriptGroup.PowerShellEvidenceDirectory);
                return false;
            }
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

            var commonProperties = WithheldKeysRemoved(properties);

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
                        CommonProperties = commonProperties,
                        ScriptProperties = new List<ScriptProperties>()
                    };

                    scriptGroups.Add(scriptGroup);
                    previousScriptGroup = scriptGroup;
                }

                previousScriptGroup.ScriptProperties.Add(new ScriptProperties()
                {
                    ScriptPath = ExtractPath(scriptsLocation, scriptApiModel),
                    Properties = WithheldKeysRemoved(ExtractProperties(scriptApiModel, commonProperties))
                });
            }

            return scriptGroups;
        }

        /// <summary>
        /// The second place the withheld keys are excluded, and deliberately not the only one.
        ///
        /// They are already excluded where configuration values become properties. This catches
        /// anything that reached the property set by another route - an environment property, a
        /// request-supplied value, or a JSON script's GenericArguments bearing the same name -
        /// before it is serialised into the script group and handed to a Runner. The script group
        /// is the artefact that crosses the process boundary, so it is the right place for the
        /// invariant to hold rather than be assumed.
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
        /// Refuses, or reports, a script path whose resolved execution target lies outside the
        /// configured script root.
        ///
        /// Both <see cref="ScriptPathsAreConfined"/> and <see cref="ExtractPath"/> resolve the
        /// stored path (direct or JSON <c>ScriptPath</c>) through <see cref="CombineWithScriptRoot"/>,
        /// which implements Windows <c>Path.Combine</c> semantics identically on Windows and Linux
        /// hosts. Confinement is therefore judged on the exact <c>ScriptProperties.ScriptPath</c>
        /// string handed to <c>PowerShellEvidence</c> and <c>Dorc.Runner</c>: rooted paths that
        /// resolve beneath <paramref name="scriptsLocation"/> succeed cleanly, while off-root,
        /// traversal, and variable-expansion escapes are reported (or refused when enforcing).
        /// </summary>
        private bool ScriptPathsAreConfined(
            ScriptApiModel script,
            string scriptsLocation,
            IDictionary<string, VariableValue> properties,
            out bool reported)
        {
            var enforcing = configurationSettingsEngine.GetScriptPathEnforcementEnabled();
            var confined = true;
            reported = false;

            if (!TryExtractRawScriptPath(script, out var rawScriptPath, out var jsonFailureReason))
            {
                confined = false;
                reported = true;
                Report(enforcing, script.Path, scriptsLocation, jsonFailureReason);
                return !enforcing;
            }

            var executionPath = CombineWithScriptRoot(scriptsLocation, rawScriptPath);

            if (!string.IsNullOrWhiteSpace(rawScriptPath) && HasTraversalOrEncodedTraversal(rawScriptPath))
            {
                confined = false;
                reported = true;
                Report(enforcing, executionPath, scriptsLocation,
                    "it contains a parent directory reference ('..'), which can resolve outside the script root.");
            }
            else if (!PathConfinement.IsWithin(executionPath, scriptsLocation))
            {
                confined = false;
                reported = true;
                Report(enforcing, executionPath, scriptsLocation,
                    "the resolved execution path does not lie beneath the script root.");
            }
            else if (!string.IsNullOrWhiteSpace(rawScriptPath))
            {
                var expandedScriptPath = PropertyTokenExpander.Expand(rawScriptPath, properties);
                if (PropertyTokenExpander.ContainsUnexpandedDropFolderToken(expandedScriptPath))
                {
                    confined = false;
                    reported = true;
                    Report(enforcing, script.Path, scriptsLocation,
                        "it references an unexpanded DropFolder token that cannot be shown to resolve beneath the script root.");
                }
                else if (!string.Equals(expandedScriptPath, rawScriptPath, StringComparison.Ordinal))
                {
                    var expandedExecutionPath = CombineWithScriptRoot(scriptsLocation, expandedScriptPath);
                    if (HasTraversalOrEncodedTraversal(expandedScriptPath)
                        || !PathConfinement.IsWithin(expandedExecutionPath, scriptsLocation))
                    {
                        confined = false;
                        reported = true;
                        Report(enforcing, script.Path, scriptsLocation,
                            "the resolved path after variable expansion does not lie beneath the script root.");
                    }
                }
            }

            // Reporting mode reports and proceeds. Only enforcement stops the deployment.
            return confined || !enforcing;
        }

        private bool ResolvedScriptGroupPathsAreConfined(
            IEnumerable<ScriptGroup> scriptGroups,
            string scriptsLocation,
            bool alreadyReported)
        {
            var enforcing = configurationSettingsEngine.GetScriptPathEnforcementEnabled();
            var confined = true;

            foreach (var scriptGroup in scriptGroups)
            {
                foreach (var scriptProperties in scriptGroup.ScriptProperties)
                {
                    if (!string.IsNullOrWhiteSpace(scriptProperties.ScriptPath)
                        && PathConfinement.IsWithin(scriptProperties.ScriptPath, scriptsLocation))
                    {
                        continue;
                    }

                    confined = false;
                    if (!alreadyReported)
                    {
                        Report(enforcing, scriptProperties.ScriptPath, scriptsLocation,
                            "the resolved execution path does not lie beneath the script root.");
                    }
                }
            }

            return confined || !enforcing;
        }

        internal static string CombineWithScriptRoot(string scriptsLocation, string? scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath))
            {
                return scriptsLocation;
            }

            if (string.IsNullOrEmpty(scriptsLocation) || IsWindowsRootedOrDriveQualified(scriptPath))
            {
                return scriptPath;
            }

            return scriptsLocation.TrimEnd('\\', '/') + '\\' + scriptPath;
        }

        private static bool IsWindowsRootedOrDriveQualified(string path)
        {
            if (path[0] == '\\' || path[0] == '/')
            {
                return true;
            }

            return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
        }

        private static bool HasTraversalOrEncodedTraversal(string path)
        {
            if (path.Split('\\', '/').Any(segment => segment.Trim() == ".."))
            {
                return true;
            }

            return path.Contains("%2e", StringComparison.OrdinalIgnoreCase)
                || path.Contains("%2f", StringComparison.OrdinalIgnoreCase)
                || path.Contains("%5c", StringComparison.OrdinalIgnoreCase)
                || path.Contains("%25", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractRawScriptPath(
            ScriptApiModel script,
            out string rawScriptPath,
            out string failureReason)
        {
            rawScriptPath = string.Empty;
            failureReason = string.Empty;

            if (!script.IsPathJSON)
            {
                rawScriptPath = script.Path ?? string.Empty;
                return true;
            }

            if (string.IsNullOrWhiteSpace(script.Path))
            {
                failureReason = "it is marked as a JSON script path but is empty.";
                return false;
            }

            try
            {
                if (JsonNode.Parse(script.Path) is not JsonObject document)
                {
                    failureReason = "it is marked as a JSON script path but is not a JSON object.";
                    return false;
                }

                var matchingProperties = document
                    .Where(pair => string.Equals(pair.Key, nameof(JSONPath.ScriptPath), StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingProperties.Count > 1)
                {
                    failureReason = "it is a JSON script path with conflicting ScriptPath properties.";
                    return false;
                }

                if (matchingProperties.Count == 1)
                {
                    var prop = matchingProperties[0];
                    if (!string.Equals(prop.Key, nameof(JSONPath.ScriptPath), StringComparison.Ordinal))
                    {
                        failureReason = "it is a JSON script path whose ScriptPath property casing does not match the execution contract.";
                        return false;
                    }

                    if (prop.Value != null
                        && (prop.Value is not JsonValue jsonVal || !jsonVal.TryGetValue<string>(out _)))
                    {
                        failureReason = "it is a JSON script path whose ScriptPath is not a string.";
                        return false;
                    }
                }

                var parsed = JsonSerializer.Deserialize<JSONPath>(script.Path);
                if (parsed == null)
                {
                    failureReason = "the JSON script path deserialised to null.";
                    return false;
                }

                rawScriptPath = parsed.ScriptPath ?? string.Empty;
                return true;
            }
            catch (JsonException)
            {
                failureReason = "it is marked as a JSON script path but is not valid JSON.";
                return false;
            }
        }

        /// <summary>
        /// Puts the script's content baseline into the group the runner will receive, recording
        /// one from the share when none exists yet.
        ///
        /// The baseline comes from the frozen <see cref="ScriptApiModel"/> snapshot captured when
        /// the deployment request was submitted, falling back to a live lookup only for legacy
        /// pre-snapshot requests that did not record <see cref="ScriptApiModel.Id"/>. First-use
        /// recording is pinned to the snapshot's <see cref="ScriptApiModel.Path"/> and applies
        /// only to PowerShell share scripts.
        /// </summary>
        private void ApplyContentBaseline(
            ScriptApiModel script,
            string scriptsLocation,
            IList<ScriptGroup> scriptGroups)
        {
            var mode = configurationSettingsEngine.GetScriptContentVerificationMode();

            var shareScriptGroups = new List<ScriptGroup>();
            foreach (var scriptGroup in scriptGroups)
            {
                if (scriptGroup.TerraformSourceType != TerraformSourceType.SharedFolder)
                {
                    scriptGroup.ContentVerification = ScriptContentVerificationMode.Off;
                    continue;
                }

                scriptGroup.ContentVerification = mode;
                shareScriptGroups.Add(scriptGroup);
            }

            if (mode == ScriptContentVerificationMode.Off || shareScriptGroups.Count == 0)
            {
                return;
            }

            var scriptId = script.Id;
            var expectedPath = script.Path;
            var baseline = script.ContentHash;

            if (string.IsNullOrWhiteSpace(baseline)
                && scriptId <= 0
                && !string.IsNullOrWhiteSpace(expectedPath))
            {
                var liveScript = LookupLiveScriptByPath(expectedPath);
                if (liveScript != null)
                {
                    scriptId = liveScript.Id;
                    baseline = liveScript.ContentHash;
                }
            }

            if (string.IsNullOrWhiteSpace(baseline))
            {
                if (mode == ScriptContentVerificationMode.Report && scriptId > 0)
                {
                    baseline = RecordFirstSeenBaseline(
                        scriptId,
                        expectedPath,
                        scriptsLocation,
                        shareScriptGroups);
                }
                else if (mode == ScriptContentVerificationMode.Enforce)
                {
                    logger.LogWarning(
                        "Script {ScriptId} ('{ScriptPath}') has no recorded content baseline while"
                        + " ScriptContentVerification is set to Enforce. Skipping trust-on-first-use"
                        + " capture; the runner will refuse execution until a baseline is recorded.",
                        scriptId,
                        LogText.SingleLine(expectedPath));
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(baseline))
            {
                return;
            }

            foreach (var scriptGroup in shareScriptGroups)
            {
                foreach (var scriptProperties in scriptGroup.ScriptProperties)
                {
                    scriptProperties.ExpectedContentHash = baseline;
                }
            }
        }

        private ScriptApiModel? LookupLiveScriptByPath(string expectedPath)
        {
            var page = _scriptsPersistentSource.GetScriptsByPage(
                int.MaxValue,
                1,
                new PagedDataOperators
                {
                    Filters = new List<PagedDataFilter>
                    {
                        new PagedDataFilter { Path = nameof(ScriptApiModel.Path), FilterValue = expectedPath }
                    }
                });

            return page?.Items?.FirstOrDefault(
                candidate => string.Equals(candidate.Path, expectedPath, StringComparison.Ordinal));
        }

        /// <summary>
        /// Trust on first use in <see cref="ScriptContentVerificationMode.Report"/> mode. Worth
        /// being honest about what this is and is not: it does not establish that the script was
        /// legitimate when first seen — if the share was already compromised, the compromised
        /// bytes become the baseline. What it detects is change *after* recording, which is the
        /// actual attack, and it is what makes the control adoptable during the Report rollout
        /// without an inventory of what every script in the estate currently is.
        ///
        /// Monitor-side reads are strictly restricted to paths confined beneath
        /// <paramref name="scriptsLocation"/> so an off-root UNC path cannot coerce the Monitor
        /// service identity into connecting to an untrusted host while path confinement is in
        /// report mode.
        /// </summary>
        private string? RecordFirstSeenBaseline(
            int scriptId,
            string? expectedPath,
            string scriptsLocation,
            IEnumerable<ScriptGroup> scriptGroups)
        {
            var path = scriptGroups
                .SelectMany(group => group.ScriptProperties)
                .Select(properties => properties.ScriptPath)
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(scriptsLocation)
                || HasTraversalOrEncodedTraversal(path)
                || !PathConfinement.IsWithin(path, scriptsLocation)
                || HasReparsePointBeneathRoot(path, scriptsLocation))
            {
                logger.LogWarning(
                    "Skipping trust-on-first-use baseline capture for script {ScriptId}"
                    + " ('{ScriptPath}') because it does not lie within configured script root"
                    + " '{ScriptRoot}' or traverses a reparse point.",
                    scriptId,
                    LogText.SingleLine(path),
                    LogText.SingleLine(scriptsLocation));
                return null;
            }

            string baseline;
            try
            {
                baseline = ScriptContentHash.ComputeFromFile(path);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or ArgumentException
                or NotSupportedException)
            {
                // Reading the share can fail for every ordinary reason. The deployment is about
                // to fail on its own for the same reason and say so more usefully.
                logger.LogWarning(ex,
                    "Could not read '{ScriptPath}' to record a content baseline. The script will be"
                    + " dispatched without one.",
                    LogText.SingleLine(path));

                return null;
            }

            // A synthetic principal, because there is no user here — this is the deployment
            // engine baselining a script, and the audit entry should say so. The Monitor's
            // claims reader reports the service's own configured identity whatever it is
            // handed, so the name below is what makes the intent legible in code rather than
            // what lands in the audit trail.
            var engine = new GenericPrincipal(new GenericIdentity("DOrc deployment engine"), null);

            var authoritativeBaseline = string.IsNullOrWhiteSpace(expectedPath)
                ? _scriptsPersistentSource.RecordContentHashIfUnrecorded(scriptId, baseline, engine)
                : _scriptsPersistentSource.RecordContentHashIfUnrecorded(
                    scriptId, expectedPath, baseline, engine);

            if (string.IsNullOrWhiteSpace(authoritativeBaseline))
            {
                logger.LogWarning(
                    "Could not record a content baseline for script {ScriptId} ('{ScriptPath}')."
                    + " It will be verified from whenever one is recorded.",
                    scriptId, path);

                return null;
            }

            if (ScriptContentHash.Matches(authoritativeBaseline, baseline))
            {
                logger.LogInformation(
                    "Recorded a first-seen content baseline for script {ScriptId}"
                    + " ('{ScriptPath}'). Subsequent deployments verify against it.",
                    scriptId, path);
            }
            else
            {
                logger.LogInformation(
                    "Another deployment recorded the first content baseline for script"
                    + " {ScriptId} ('{ScriptPath}'). This deployment will verify against that"
                    + " authoritative baseline.",
                    scriptId, path);
            }

            return authoritativeBaseline;
        }

        private static bool HasReparsePointBeneathRoot(string candidatePath, string rootPath)
        {
            var directoryPath = Path.GetDirectoryName(candidatePath);
            var fileName = Path.GetFileName(candidatePath);
            if (string.IsNullOrWhiteSpace(directoryPath)
                || directoryPath.Contains("..")
                || string.IsNullOrWhiteSpace(fileName)
                || fileName == "."
                || fileName == "..")
            {
                return true;
            }

            string rootFull;
            string current;
            try
            {
                rootFull = Path.GetFullPath(rootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var rootPrefix = rootFull + Path.DirectorySeparatorChar;
                var dirFull = Path.GetFullPath(directoryPath);
                current = Path.GetFullPath(Path.Combine(dirFull, fileName));
                if (!current.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
            {
                return true;
            }

            var isFirst = true;
            while (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    FileSystemInfo info = isFirst ? new FileInfo(current) : new DirectoryInfo(current);
                    isFirst = false;
                    if (info.Exists
                        && ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget != null))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException
                    or ArgumentException
                    or NotSupportedException)
                {
                    return true;
                }

                if (string.Equals(current, rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent)
                    || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent;
            }

            return false;
        }

        private void Report(bool enforcing, string? scriptPath, string scriptsLocation, string reason)
        {
            var safeScriptPath = LogText.SingleLine(scriptPath);
            var safeScriptRoot = LogText.SingleLine(scriptsLocation);
            var safeReason = LogText.SingleLine(reason);

            if (enforcing)
            {
                logger.LogError(
                    "Refusing to execute '{ScriptPath}' against script root '{ScriptRoot}', because"
                    + " {Reason} Register the script beneath the root, or correct the component's"
                    + " stored path.",
                    safeScriptPath, safeScriptRoot, safeReason);
            }
            else
            {
                logger.LogWarning(
                    "Script path '{ScriptPath}' would be refused once confinement is enforced,"
                    + " because {Reason} Executing it because 'EnforceScriptPathConfinement' is not"
                    + " enabled. Script root: '{ScriptRoot}'.",
                    safeScriptPath, safeReason, safeScriptRoot);
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
                return CombineWithScriptRoot(scriptsLocation, scriptApiModel.Path ?? string.Empty);
            }

            logger.LogInformation("Found a JSON script path.");

            if (string.IsNullOrWhiteSpace(scriptApiModel.Path))
            {
                return string.Empty;
            }

            JSONPath? deserializeObject;
            try
            {
                deserializeObject = JsonSerializer.Deserialize<JSONPath>(scriptApiModel.Path);
            }
            catch (JsonException)
            {
                return string.Empty;
            }

            if (deserializeObject == null)
            {
                return string.Empty;
            }

            return CombineWithScriptRoot(scriptsLocation, deserializeObject.ScriptPath);
        }

        private IDictionary<string, VariableValue> ExtractProperties(ScriptApiModel scriptApiModel,
            IDictionary<string, VariableValue> commonProperties)
        {
            var properties = new Dictionary<string, VariableValue>();
            if (!scriptApiModel.IsPathJSON)
            {
                return properties;
            }

            JSONPath? deserializeObject;
            try
            {
                deserializeObject = JsonSerializer.Deserialize<JSONPath>(scriptApiModel.Path);
            }
            catch (JsonException)
            {
                return properties;
            }

            if (deserializeObject?.GenericArguments == null)
            {
                return properties;
            }

            foreach (var deserializeObjectParam in deserializeObject.GenericArguments)
            {
                var first = deserializeObjectParam.Values.First();
                var text = first?.ToString();
                if (text != null && System.Text.RegularExpressions.Regex.IsMatch(text, @"^\$\{[A-Za-z0-9_.-]+\}$"))
                {
                    var propertyName = text.Substring(2, text.Length - 3);
                    if (!commonProperties.TryGetValue(propertyName, out var resolved))
                        throw new InvalidOperationException("A referenced script property is unavailable.");
                    properties.TryAdd(deserializeObjectParam.Keys.First(), resolved);
                    continue;
                }
                properties.TryAdd(deserializeObjectParam.Keys.First(), new VariableValue { Value = first, Type = first.GetType() });
            }

            return properties;
        }
    }
}
