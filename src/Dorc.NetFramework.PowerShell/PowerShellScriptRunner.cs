using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Dorc.ApiModel;

namespace Dorc.NetFramework.PowerShell
{
    public class PowerShellScriptRunner : IPowerShellScriptRunner
    {
        private readonly IRunnerLogger logger;
        private int deploymentResultId;
        private readonly ScriptGroup evidenceContract;
        private int ordinal;
        private IReadOnlyList<string> sensitiveValues = Array.Empty<string>();

        public StringWriter OutputObjects { get; private set; }

        public PowerShellScriptRunner(IRunnerLogger logger, int deploymentResultId, ScriptGroup evidenceContract = null)
        {
            this.logger = logger;
            this.deploymentResultId = deploymentResultId;
            this.evidenceContract = evidenceContract;
            ordinal = evidenceContract?.EvidenceOrdinalOffset ?? 0;
        }

        public void Run(string scriptsLocation,
            IEnumerable<(string, IDictionary<string, VariableValue>)> scripts,
            IDictionary<string, VariableValue> commonProperties)
        {
            foreach ((string, IDictionary<string, VariableValue>) script in scripts)
            {
                string scriptName = script.Item1;
                logger.FileLogger.LogInformation("\tStarting execution of script '" + scriptName + "'.");

                IDictionary<string, VariableValue> scriptProperties = script.Item2;
                IDictionary<string, VariableValue> combinedProperties = this.CombineProperties(scriptProperties, commonProperties);
                this.sensitiveValues = CollectSensitiveValues(combinedProperties);
                try
                {

                    var host = new CustomHost();
                    using (var runspace = RunspaceFactory.CreateRunspace(host, InitialSessionState.CreateDefault()))
                    {
                        runspace.InitialSessionState.AuthorizationManager = new AuthorizationManager("Microsoft.PowerShell");
                        runspace.Open();

                        this.AddProperties(runspace, combinedProperties);

                        using (var powerShell = System.Management.Automation.PowerShell.Create())
                        {
                            host.HostUserInterface.MessageAdded += (sender, e) =>
                            {
                                LogMessage(e.Message, e.MessageType);
                            };
                            powerShell.Runspace = runspace;

                            if (!string.IsNullOrEmpty(scriptsLocation))
                            {
                                powerShell.AddCommand("Set-Location").AddParameter("Path", scriptsLocation);
                            }

                            var input = new PowerShellInput(scriptName, evidenceContract?.PowerShellEvidenceDirectory,
                                evidenceContract?.DefinitionRequestId ?? 0, deploymentResultId,
                                evidenceContract?.DefinitionRevision, ordinal++, evidenceContract?.PowerShellVersionNumber,
                                runspace.Version.ToString());
                            powerShell.AddScript(input.Text);
                            powerShell.InvocationStateChanged += (_, state) =>
                            {
                                if (state.InvocationStateInfo.State == PSInvocationState.Running) input.InvocationObserved();
                            };
                            logger.FileLogger.LogInformation($"Adding Script for execution '{scriptName}'.");

                            // create a data collection for standard output
                            var outputCollection = new PSDataCollection<PSObject>();
                            // and register the event handler on that too
                            outputCollection.DataAdded += (sender, e) =>
                            {
                                var data = sender as PSDataCollection<PSObject>;
                                var msg = GetOutput(data[e.Index]);
                                if (string.IsNullOrWhiteSpace(msg)) return;
                                LogMessage(msg, MessageType.None);
                            };

                            //Add only Error Stream because all other streams supported by HostUserInterface
                            powerShell.Streams.Error.DataAdded += Powershell_Error_DataAdded;
                            outputCollection.DataAdded += Powershell_Output_DataAdded;

                            try
                            {
                                logger.FileLogger.LogInformation($"Execution of the powershell Script {scriptName} is beginning");
                                powerShell.Invoke(null, outputCollection);
                                input.Completed();
                                logger.FileLogger.LogInformation($" Execution of the powershell Script {scriptName} has completed");
                            }
                            catch (Exception exception)
                            {
                                string exceptionMessage = this.GetExceptionMessage(powerShell);

                                if (string.IsNullOrEmpty(exceptionMessage))
                                {
                                    throw new InvalidOperationException(RedactSensitiveValues(exception.Message));
                                }
                                exceptionMessage = RedactSensitiveValues(exceptionMessage);
                                var redactedReason = RedactSensitiveValues(exception.Message);
                                logger.FileLogger.LogInformation($"Execution of the powershell Script {scriptName} has Errored : {exceptionMessage}");
                                throw new RemoteException(exceptionMessage, new InvalidOperationException(redactedReason));
                            }

                            logger.FileLogger.LogDebug("Checking runspace State");

                            if (runspace.RunspaceStateInfo != null
                                && runspace.RunspaceStateInfo.State == RunspaceState.Broken)
                            {
                                throw new Exception(
                                    $"The runspace has been disconnected abnormally. Reason: {runspace.RunspaceStateInfo.Reason}");
                            }
                            logger.FileLogger.LogDebug("Checking runspace State...Done");
                            logger.FileLogger.LogDebug("Checking InvocationStateInfo");
                            if (powerShell.InvocationStateInfo != null
                                && powerShell.InvocationStateInfo.State == PSInvocationState.Failed)
                            {
                                throw new Exception("PowerShell completed abnormally due to an error. Reason: " + powerShell.InvocationStateInfo.Reason);
                            }
                            if (host.ShouldExit && host.ExitCode != 0)
                            {
                                throw new Exception($"PowerShell script exited with non-zero exit code: {host.ExitCode}");
                            }
                            if (TryGetNonZeroLastExitCode(runspace, out int lastExitCode))
                            {
                                throw new Exception($"PowerShell script completed with non-zero $LASTEXITCODE: {lastExitCode}");
                            }
                            logger.FileLogger.LogDebug("Checking InvocationStateInfo...Done");
                        }
                    }

                }
                catch (Exception e)
                {
                    var sanitizedError = new InvalidOperationException(RedactSensitiveValues(e.ToString()));
                    logger.FileLogger.LogError(sanitizedError, $"Exception occured in the powershell execution of script {scriptName}");
                    throw sanitizedError;
                }
                logger.FileLogger.LogInformation($" Execution of the powershell Script {scriptName} was successful");
            }
        }

        private void LogMessage(string msg, MessageType type = MessageType.None)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(msg)) return;
                msg = RedactSensitiveValues(msg);
                switch (type)
                {
                    case MessageType.Info:
                        logger.Information(msg);
                        break;
                    case MessageType.Verbose:
                        logger.Verbose(msg);
                        break;
                    case MessageType.Warning:
                        logger.Warning(msg);
                        break;
                    case MessageType.Error:
                        logger.Error(msg);
                        break;
                    case MessageType.Debug:
                        logger.Debug(msg);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Exception Occured logging Information Message from powershell execution");
            }
        }

        void Powershell_Error_DataAdded(object sender, DataAddedEventArgs e)
        {
            var data = (PSDataCollection<ErrorRecord>)sender;
            var msg = GetErrorRecordData(data[e.Index]);
            LogMessage(msg, MessageType.Error);
        }

        private void Powershell_Output_DataAdded(object sender, DataAddedEventArgs e)
        {
            try
            {
                var data = (PSDataCollection<PSObject>)sender;
                var msg = GetOutput(data[e.Index]);
                if (string.IsNullOrWhiteSpace(msg)) return;
                logger.Information(RedactSensitiveValues(msg));
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Exception Occured logging Information Message from powershell execution");
            }
        }

        private string GetOutput(object o)
        {
            switch (o)
            {
                case null:
                    return "<null>";
                case PSObject psObject:
                    return GetOutput(psObject.BaseObject);
                case string _:
                    return o.ToString();
                default:
                    return o.ToString();
            }
        }

        private IDictionary<string, VariableValue> CombineProperties(
            IDictionary<string, VariableValue> scriptProperties,
            IDictionary<string, VariableValue> commonProperties)
        {
            var combinedProperties = new Dictionary<string, VariableValue>();

            foreach (var scriptProperty in scriptProperties)
            {
                combinedProperties.Add(scriptProperty.Key, scriptProperty.Value);
            }

            foreach (var commonProperty in commonProperties)
            {
                if (combinedProperties.ContainsKey(commonProperty.Key))
                {
                    continue;
                }
                combinedProperties.Add(commonProperty.Key, commonProperty.Value);
            }

            return combinedProperties;
        }

        private IReadOnlyList<string> CollectSensitiveValues(IDictionary<string, VariableValue> properties)
        {
            var values = new List<string>();
            if (properties == null) return values;

            var explicitNames = new HashSet<string>(
                evidenceContract?.SensitivePropertyNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in properties)
            {
                var isExplicitlySecret = kvp.Key != null && explicitNames.Contains(kvp.Key);
                var matchesKeyNameHeuristic = kvp.Key != null
                    && explicitNames.Count == 0
                    && (kvp.Key.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        kvp.Key.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        kvp.Key.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        kvp.Key.IndexOf("ApiKey", StringComparison.OrdinalIgnoreCase) >= 0);

                if (kvp.Value?.Value is IEnumerable<string> arrayElements)
                {
                    foreach (var element in arrayElements.Where(element => !string.IsNullOrWhiteSpace(element)))
                    {
                        if ((isExplicitlySecret || (matchesKeyNameHeuristic && element.Length >= 4 && !long.TryParse(element, out _)))
                            && !values.Contains(element))
                        {
                            values.Add(element);
                        }
                    }
                    continue;
                }

                var raw = kvp.Value?.Value?.ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var isHeuristicSecret = matchesKeyNameHeuristic
                    && raw.Length >= 4
                    && !long.TryParse(raw, out _);
                if (!isExplicitlySecret && !isHeuristicSecret) continue;

                if (!values.Contains(raw))
                {
                    values.Add(raw);
                }

                if (raw.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    try
                    {
                        var elements = JsonConvert.DeserializeObject<string[]>(raw);
                        if (elements != null)
                        {
                            foreach (var element in elements.Where(element => !string.IsNullOrWhiteSpace(element) && !values.Contains(element)))
                            {
                                values.Add(element);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Value is not a JSON string array; full string is already collected
                    }
                }
            }
            values.Sort((a, b) => b.Length.CompareTo(a.Length));
            return values;
        }

        private string RedactSensitiveValues(string message)
        {
            if (string.IsNullOrEmpty(message) || sensitiveValues.Count == 0) return message;
            var redacted = message;
            foreach (var secret in sensitiveValues)
            {
                redacted = redacted.Replace(secret, "[REDACTED]");
            }
            return redacted;
        }

        private void AddProperties(Runspace runspace, IDictionary<string, VariableValue> properties)
        {
            foreach (var property in properties)
            {
                try
                {
                    runspace.SessionStateProxy.SetVariable(property.Key, property.Value.Value);
                }
                catch (Exception ex)
                {
                    // The property NAME only. Serialising the value here wrote secrets to the
                    // runner log and to standard output - and the values most likely to fail
                    // to bind are the unusual ones, which include credentials.
                    logger.FileLogger.LogError(ex,
                        "Unable to set variable '{PropertyName}' in PowerShell Session", property.Key);
                    Console.WriteLine(
                        $"Unable to set variable '{property.Key}' in PowerShell Session: {ex.Message}");
                }
            }
        }

        private string GetExceptionMessage(System.Management.Automation.PowerShell powerShell)
        {
            string exceptionMessage = string.Empty;

            string errorInformation = this.GetErrorInformation(powerShell);

            if (!string.IsNullOrEmpty(errorInformation))
            {
                exceptionMessage = errorInformation;
            }

            string debugInformation = this.GetDebugInformation(powerShell);

            if (!string.IsNullOrEmpty(debugInformation))
            {
                if (!string.IsNullOrEmpty(exceptionMessage))
                {
                    exceptionMessage += Environment.NewLine + debugInformation;
                }
                else
                {
                    exceptionMessage = debugInformation;
                }
            }

            return exceptionMessage;
        }

        private string GetErrorInformation(System.Management.Automation.PowerShell powerShell)
        {
            string errorInformation = string.Empty;

            if (powerShell == null
                || powerShell.Streams == null
                || powerShell.Streams.Error == null
                || powerShell.Streams.Error.Count == 0)
            {
                return errorInformation;
            }

            foreach (ErrorRecord errorRecord in powerShell.Streams.Error)
            {
                string errorRecordData = this.GetErrorRecordData(errorRecord);

                if (string.IsNullOrEmpty(errorInformation))
                {
                    errorInformation = errorRecordData;
                    continue;
                }
                errorInformation += Environment.NewLine + errorRecordData;
            }

            return errorInformation;
        }

        private string GetErrorRecordData(ErrorRecord errorRecord)
        {
            return "ErrorId: " + errorRecord?.FullyQualifiedErrorId + Environment.NewLine
                + "Exception: " + errorRecord?.Exception + Environment.NewLine
                + "ScriptStackTrace: " + errorRecord?.ScriptStackTrace + Environment.NewLine
                + "InvocationInfo.PositionMessage: " + errorRecord?.InvocationInfo?.PositionMessage + Environment.NewLine
                + "ErrorDetails: " + errorRecord?.ErrorDetails + Environment.NewLine
                + "TargetObject: " + errorRecord?.TargetObject + Environment.NewLine;
        }

        private string GetDebugInformation(System.Management.Automation.PowerShell powerShell)
        {
            string debugInformation = string.Empty;

            if (powerShell == null
                || powerShell.Streams == null
                || powerShell.Streams.Information == null
                || powerShell.Streams.Information.Count == 0)
            {
                return debugInformation;
            }

            foreach (InformationRecord informationRecord in powerShell.Streams.Information)
            {
                var messageData = informationRecord.MessageData;
                if (messageData == null)
                {
                    continue;
                }
                string message = messageData.ToString();

                if (string.IsNullOrEmpty(message))
                {
                    continue;
                }

                bool isUnexpectedErrorMessage = message.Contains("Unexpected Error:");
                if (isUnexpectedErrorMessage)
                {
                    break;
                }

                if (string.IsNullOrEmpty(debugInformation))
                {
                    debugInformation = message;
                    continue;
                }

                debugInformation += Environment.NewLine + message;
            }

            return debugInformation;
        }

        private static bool TryGetNonZeroLastExitCode(Runspace runspace, out int exitCode)
        {
            exitCode = 0;
            try
            {
                var raw = runspace.SessionStateProxy.GetVariable("LASTEXITCODE");
                if (raw is PSObject psObj)
                {
                    raw = psObj.BaseObject;
                }
                if (raw is int code && code != 0)
                {
                    exitCode = code;
                    return true;
                }
            }
            catch (PSInvalidOperationException)
            {
                // Ignore if runspace variable lookup is unavailable
            }
            catch (ObjectDisposedException)
            {
                // Ignore if runspace variable lookup is unavailable
            }
            catch (InvalidOperationException)
            {
                // Ignore if runspace variable lookup is unavailable
            }
            catch (RuntimeException)
            {
                // Ignore if runspace variable lookup is unavailable
            }
            return false;
        }


    }
}