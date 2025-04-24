using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;

namespace Dorc.NetFramework.PowerShell
{
    public class PowerShellScriptRunner : IPowerShellScriptRunner
    {
        private readonly IRunnerLogger logger;
        private int deploymentResultId;

        public StringWriter OutputObjects { get; private set; }

        public PowerShellScriptRunner(IRunnerLogger logger, int deploymentResultId)
        {
            this.logger = logger;
            this.deploymentResultId = deploymentResultId;
        }

        public void Run(string scriptsLocation,
            IEnumerable<(string, IDictionary<string, VariableValue>)> scripts,
            IDictionary<string, VariableValue> commonProperties)
        {
            foreach ((string, IDictionary<string, VariableValue>) script in scripts)
            {
                string scriptName = script.Item1;
                logger.Information("\tStarting execution of script '" + scriptName + "'.");

                IDictionary<string, VariableValue> scriptProperties = script.Item2;
                IDictionary<string, VariableValue> combinedProperties = this.CombineProperties(scriptProperties, commonProperties);
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
                            host.HostUserInterface.MessageAdded += HostUserInterface_MessageAdded;
                            powerShell.Runspace = runspace;

                            if (!string.IsNullOrEmpty(scriptsLocation))
                            {
                                powerShell.AddCommand("Set-Location").AddParameter("Path", scriptsLocation);
                            }

                            powerShell.AddScript(File.ReadAllText(scriptName));
                            logger.Information($"Adding Script for execution '{scriptName}'.");

                            // create a data collection for standard output
                            //var outputCollection = new PSDataCollection<PSObject>();
                            // and register the event handler on that too
                            //outputCollection.DataAdded += Powershell_Output_DataAdded;

                            try
                            {
                                logger.Information($"Execution of the powershell Script {scriptName} is beginning");
                                //powerShell.Invoke(null, outputCollection);
                                powerShell.Invoke();
                                logger.Information($" Execution of the powershell Script {scriptName} has completed");
                            }
                            catch (Exception exception)
                            {
                                string exceptionMessage = this.GetExceptionMessage(powerShell);

                                if (string.IsNullOrEmpty(exceptionMessage))
                                {
                                    throw;
                                }
                                logger.Information($"Execution of the powershell Script {scriptName} has Errored : {exceptionMessage}");
                                throw new RemoteException(exceptionMessage, exception);
                            }

                            logger.Debug("Checking runspace State");

                            if (runspace.RunspaceStateInfo != null
                                && runspace.RunspaceStateInfo.State == RunspaceState.Broken)
                            {
                                throw new Exception(
                                    $"The runspace has been disconnected abnormally. Reason: {runspace.RunspaceStateInfo.Reason}");
                            }
                            logger.Debug("Checking runspace State...Done");
                            logger.Debug("Checking InvocationStateInfo");
                            if (powerShell.InvocationStateInfo != null
                                && powerShell.InvocationStateInfo.State == PSInvocationState.Failed)
                            {
                                throw new Exception("PowerShell completed abnormally due to an error. Reason: " + powerShell.InvocationStateInfo.Reason);
                            }
                            logger.Debug("Checking InvocationStateInfo...Done");
                        }
                    }

                }
                catch (Exception e)
                {
                    logger.Error(e, $"Exception occured in the powershell execution of script {scriptName}");
                    throw;
                }
                logger.Information($" Execution of the powershell Script {scriptName} was successful");
            }
        }

        private void HostUserInterface_MessageAdded(object sender, MessageAddedEventArgs e)
        {
            try
            {
                var msg = e.Message;
                if (string.IsNullOrWhiteSpace(msg)) return;
                switch (e.MessageType)
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
                logger.UpdateLog(deploymentResultId, msg);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Exception Occured logging Information Message from powershell execution");
            }
        }

        private void Powershell_Output_DataAdded(object sender, DataAddedEventArgs e)
        {
            try
            {
                var data = (PSDataCollection<PSObject>)sender;
                var msg = GetOutput(data[e.Index]);
                if (string.IsNullOrWhiteSpace(msg)) return;
                logger.Information(msg);
                logger.UpdateLog(deploymentResultId, msg);
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
                    logger.Error($"Unable to set variable '{property.Key}' in PowerShell Session with value '{val}'",
                        ex);
                    Console.WriteLine(
                        $"Unable to set variable '{property.Key}' in PowerShell Session with value '{val}': {ex}");
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


    }
}