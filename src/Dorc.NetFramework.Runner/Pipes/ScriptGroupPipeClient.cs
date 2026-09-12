using System;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Microsoft.Extensions.Logging;

namespace Dorc.NetFramework.Runner.Pipes
{
    /// <summary>
    /// Reads the script group from the Monitor's named pipe.
    ///
    /// Connecting to a name is not the same as knowing who is on the other end. The pipe name
    /// is composed from the machine name and the request id and appears on this process's own
    /// command line, so it is not a secret; whoever creates it first owns it. A local process
    /// that won that race could previously feed this Runner an entire script group of its own
    /// authorship - ScriptsLocation and ScriptPath included, which is arbitrary code as the
    /// deployment account. The pipe's owner is now checked against the identity the Monitor
    /// stated when it started this process, before a single byte is deserialised.
    /// </summary>
    internal class ScriptGroupPipeClient : IScriptGroupPipeClient
    {
        private readonly ILogger logger;
        private readonly string expectedServerSid;

        internal ScriptGroupPipeClient(ILogger logger, string expectedServerSid)
        {
            this.logger = logger;
            this.expectedServerSid = expectedServerSid;
        }

        public ScriptGroup GetScriptGroupProperties(string scriptGroupPipeName)
        {
            logger.LogInformation("Pipe name provided for client: '" + scriptGroupPipeName + "'.");

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    logger.LogInformation("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            logger.LogInformation("Client pipe is connected.");
                        }
                        else
                        {
                            logger.LogWarning("Client pipe is NOT connected.");
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError("Exception is thrown while trying to connect to named pipe sever: {0} " , e);
                        throw;
                    }

                    VerifyServerIdentity(pipeClient, scriptGroupPipeName);

                    logger.LogInformation("Deserializing received ScriptGroup.");
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters =
                        {
                            new VariableValueJsonConverter(),
                        }
                    };

                    ScriptGroup scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(pipeClient, options);

                    var guid = scriptGroup.ID.ToString();
                    var list = scriptGroup.ScriptProperties.ToList();
                    var env = scriptGroup.CommonProperties["EnvironmentName"];

                    logger.LogInformation($"Received from pipe: {guid}");
                    foreach (var scriptGroupScriptProperty in list)
                    {
                        var propertyNames = string.Join(", ", scriptGroupScriptProperty.Properties.Keys);

                        logger.LogInformation($"Asked to execute: {scriptGroupScriptProperty.ScriptPath} for env {env.Value} with properties: {propertyNames}");
                    }

                    logger.LogInformation("Deserialization of ScriptGroup is completed.");

                    return scriptGroup;
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Client pipe has failed. Exception: " + ex);
                throw;
            }
        }

        private void VerifyServerIdentity(NamedPipeClientStream pipeClient, string pipeName)
        {
            var owner = pipeClient.GetAccessControl().GetOwner(typeof(SecurityIdentifier));

            RequireOwnedBy(expectedServerSid, owner == null ? null : owner.Value, pipeName);

            logger.LogInformation("Script group pipe ownership verified against the expected server identity.");
        }

        /// <summary>
        /// Refuses a pipe that is not owned by the account the Monitor said would own it.
        ///
        /// Absence of the expected identity is a refusal, not a pass. An unverifiable server is
        /// exactly the case this check exists for, and defaulting to trust would leave the
        /// weakness open in every configuration that forgot to close it.
        ///
        /// The comparison is on canonical text rather than by constructing a security
        /// identifier from the stated value. Both sides come from the same platform API - the
        /// Monitor emits its own token's identifier and this reads the pipe's owner - so the
        /// forms are identical where they should match. It also means a malformed or absent
        /// statement simply fails to match, which is already the refusal wanted, rather than
        /// throwing something that has to be caught and converted into one.
        /// </summary>
        internal static void RequireOwnedBy(string expectedServerSid, string ownerSid, string pipeName)
        {
            if (string.IsNullOrWhiteSpace(expectedServerSid))
            {
                throw new UnauthorizedAccessException(
                    "No expected owner was supplied for script group pipe '" + pipeName + "', so the server"
                    + " cannot be authenticated. Refusing to accept a script group.");
            }

            if (!string.Equals(expectedServerSid.Trim(), ownerSid, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "Script group pipe '" + pipeName + "' is owned by '" + ownerSid + "', not by the expected '"
                    + expectedServerSid + "'. Refusing to accept a script group from it.");
            }
        }
    }
}
