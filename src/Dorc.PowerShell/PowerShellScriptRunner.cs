using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Newtonsoft.Json;
using Serilog;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Dorc.PowerShell
{
    public class PowerShellScriptRunner : IPowerShellScriptRunner
    {
        private readonly IRunnerLogger logger;
        private readonly OutputProcessor outputProcessor;

        public PowerShellScriptRunner(IRunnerLogger logger, OutputProcessor outputProc)
        {
            this.logger = logger;
            this.outputProcessor = outputProc;
        }

        public int Run(string scriptsLocation,
            string scriptName,
            IDictionary<string, VariableValue> scriptProperties,
            IDictionary<string, VariableValue> commonProperties)
        {
            logger.FileLogger.Information("Starting execution of script '{0}'", scriptName);

            IDictionary<string, VariableValue> combinedProperties = CombineProperties(scriptProperties, commonProperties);

            try
            {
                using (var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault()))
                {
                    runspace.InitialSessionState.AuthorizationManager = new AuthorizationManager("Microsoft.PowerShell");
                    runspace.Open();

                    AddProperties(runspace, combinedProperties);

                    using (var powerShell = System.Management.Automation.PowerShell.Create())
                    {
                        powerShell.Runspace = runspace;

                        if (!string.IsNullOrEmpty(scriptsLocation))
                        {
                            powerShell.AddCommand("Set-Location").AddParameter("Path", scriptsLocation);
                        }

                        powerShell.AddScript(File.ReadAllText(scriptName));

                        var outputCollection = new PSDataCollection<string>();
                        outputCollection.DataAdded += (sender, e) =>
                        {
                            var data = sender as PSDataCollection<string>;
                            var msg = data[e.Index]?.ToString();
                            logMessage(msg, MessageType.None);
                        };

                        powerShell.Streams.Information.DataAdded += Powershell_Information_DataAdded;
                        powerShell.Streams.Debug.DataAdded += Powershell_Debug_DataAdded;
                        powerShell.Streams.Warning.DataAdded += Powershell_Warning_DataAdded;
                        powerShell.Streams.Verbose.DataAdded += Powershell_Verbose_DataAdded;
                        powerShell.Streams.Error.DataAdded += Powershell_Error_DataAdded;
                        powerShell.Streams.Progress.DataAdded += Powershell_Information_DataAdded;

                        try
                        {
                            logger.FileLogger.Information("Execution of the powershell Script {0} is beginning", scriptName);
                            powerShell.Invoke(null, outputCollection);
                            logger.FileLogger.Information("Execution of the powershell Script {0} has completed", scriptName);
                        }
                        catch (Exception exception)
                        {
                            string exceptionMessage = GetExceptionMessage(powerShell);

                            if (string.IsNullOrEmpty(exceptionMessage))
                            {
                                throw;
                            }
                            logger.FileLogger.Information("Execution of the powershell Script {0} has Errored : {1}", scriptName, exception.Message);
                            throw new RemoteException(exceptionMessage, exception);
                        }

                        logger.FileLogger.Debug("Checking runspace State");

                        if (runspace.RunspaceStateInfo is { State: RunspaceState.Broken })
                        {
                            throw new Exception(
                                $"The runspace has been disconnected abnormally. Reason: {runspace.RunspaceStateInfo.Reason}");
                        }
                        logger.FileLogger.Debug("Checking runspace State...Done");
                        logger.FileLogger.Debug("Checking InvocationStateInfo");
                        if (powerShell.InvocationStateInfo != null
                            && powerShell.InvocationStateInfo.State == PSInvocationState.Failed)
                        {
                            throw new Exception("PowerShell completed abnormally due to an error. Reason: " + powerShell.InvocationStateInfo.Reason);
                        }
                        logger.FileLogger.Debug("Checking InvocationStateInfo...Done");
                    }
                }
            }
            catch (Exception e)
            {
                logger.FileLogger.Error(e, "Exception occured in the powershell execution of script {0}", scriptName);
                return -1;
            }
            finally
            {
                outputProcessor.FlushLogMessages();
            }
            logger.FileLogger.Information("Execution of the powershell Script {0} was successful", scriptName);
            return 0;
        }

        private void logMessage(string? message, MessageType type = MessageType.None)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                var psMessage = $"[PS] {message}";
                switch (type)
                {
                    case MessageType.None:
                    case MessageType.Info:
                        logger.FileLogger.Information(psMessage);
                        break;
                    case MessageType.Verbose:
                        logger.FileLogger.Verbose(psMessage);
                        break;
                    case MessageType.Warning:
                        logger.FileLogger.Warning(psMessage);
                        break;
                    case MessageType.Error:
                        logger.FileLogger.Error(psMessage);
                        break;
                    case MessageType.Debug:
                        logger.FileLogger.Debug(psMessage);
                        break;
                    default:
                        break;
                }
                outputProcessor.AddLogMessage(message);
            }
            catch (Exception exception)
            {
                logger.FileLogger.Error(exception, "Exception Occured logging Information Message from powershell execution");
            }
        }

        void Powershell_Information_DataAdded(object sender, DataAddedEventArgs e)
        {
            var data = (PSDataCollection<InformationRecord>)sender;
            var msg = data[e.Index]?.MessageData?.ToString();
            logMessage(msg, MessageType.Info);
        }

        void Powershell_Verbose_DataAdded(object sender, DataAddedEventArgs e)
        {
            var data = (PSDataCollection<VerboseRecord>)sender;
            var msg = data[e.Index]?.Message;
            logMessage(msg, MessageType.Verbose);
        }

        void Powershell_Debug_DataAdded(object sender, DataAddedEventArgs e)
        {
            var data = (PSDataCollection<DebugRecord>)sender;
            var msg = data[e.Index]?.Message;
            logMessage(msg, MessageType.Debug);
        }

        void Powershell_Warning_DataAdded(object sender, DataAddedEventArgs e)
        {
            var data = (PSDataCollection<WarningRecord>)sender;
            var msg = data[e.Index].Message;
            logMessage(msg, MessageType.Warning);
        }

        void Powershell_Error_DataAdded(object sender, DataAddedEventArgs e)
        {
            var data = (PSDataCollection<ErrorRecord>)sender;
            var msg = GetErrorRecordData(data[e.Index]);
            logMessage(msg, MessageType.Error);
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
                    var val = JsonConvert.SerializeObject(property.Value);
                    logger.FileLogger.Error($"Unable to set variable '{property.Key}' in PowerShell Session with value '{val}'",
                        ex);
                    Console.WriteLine(
                        $"Unable to set variable '{property.Key}' in PowerShell Session with value '{val}': {ex}");
                }
            }
        }

        private string GetExceptionMessage(System.Management.Automation.PowerShell powerShell)
        {
            string exceptionMessage = string.Empty;

            string errorInformation = GetErrorInformation(powerShell);

            if (!string.IsNullOrEmpty(errorInformation))
            {
                exceptionMessage = errorInformation;
            }

            string debugInformation = GetDebugInformation(powerShell);

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
                string errorRecordData = GetErrorRecordData(errorRecord);

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
    }
}