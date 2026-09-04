using Dorc.ApiModel;
using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Dorc.ApiModel.MonitorRunnerApi;
using AccessControlType = System.Security.AccessControl.AccessControlType;

namespace Dorc.Monitor.Pipes
{
    /// <summary>
    /// Hands the script group to the Runner over a named pipe. This is the transport Release
    /// builds ship, which makes it the one that matters.
    ///
    /// It previously created the pipe with no <see cref="PipeSecurity"/> at all, so the pipe
    /// carried the Win32 default access list and any local process could open it. With
    /// maxNumberOfServerInstances at 1, the first local connector won and received the entire
    /// cleartext bundle — every resolved deployment property plus GitHubToken,
    /// AzureBearerToken and TerraformGitPat. The pipe name is not a secret: it is composed
    /// from the machine name and the request id, and is placed on the Runner's command line.
    ///
    /// The pipe is now created with an explicit access list naming the Monitor, LocalSystem
    /// and the account the Runner will be started as, and nothing else.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal class ScriptGroupPipeServer : IScriptGroupPipeServer
    {
        /// <summary>
        /// What a Runner needs to read one script group and no more.
        ///
        /// This must be the whole of PipeAccessRights.Read, not the subset that looks
        /// sufficient. The client opens with PipeDirection.In, which becomes GENERIC_READ on
        /// the underlying CreateFile, and GENERIC_READ on a pipe expands to FILE_READ_DATA,
        /// FILE_READ_ATTRIBUTES, FILE_READ_EA and READ_CONTROL together. An access list
        /// granting only the rights the code visibly uses would omit FILE_READ_EA and the open
        /// would be denied outright — the pipe would be perfectly secured and completely
        /// unusable. READ_CONTROL is in there for a second reason: the client reads the
        /// descriptor to check who owns the pipe.
        /// </summary>
        private const PipeAccessRights RunnerRights =
            PipeAccessRights.Read | PipeAccessRights.Synchronize;

        private readonly ILogger logger;

        private ScriptGroupPipeServer() { }

        public ScriptGroupPipeServer(ILogger<ScriptGroupPipeServer> logger)
        {
            this.logger = logger;
        }

        public Task Start(
            string pipeName,
            ScriptGroup scriptGroup,
            ScriptGroupReaderIdentity readerIdentity,
            CancellationToken cancellationToken)
        {
            // Created synchronously, before Start returns. Previously the whole method body
            // ran inside Task.Factory.StartNew and the returned task was never awaited, so the
            // Runner was launched against a pipe name the Monitor had not necessarily claimed
            // yet. A local process that won that race owned the name, and the Runner would
            // take an attacker-authored script group from it — including ScriptsLocation and
            // ScriptPath, which is arbitrary code as the deployment account. Claiming the name
            // before the Runner exists closes the race in the only direction that matters: if
            // a squatter already holds the name, this throws and the deployment fails loudly
            // instead of proceeding into the squatter's hands.
            var pipeServer = CreateSecuredPipe(pipeName, readerIdentity);

            try
            {
                return Task.Run(() => Serve(pipeName, pipeServer, scriptGroup, cancellationToken));
            }
            catch
            {
                pipeServer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Nothing to withdraw: the pipe carries the script group in memory and is torn down
        /// when the server stream is disposed, so there is no artefact left on disk. The
        /// method exists because the file-backed implementation does have one.
        /// </summary>
        public void Expire(string pipeName)
        {
        }

        private NamedPipeServerStream CreateSecuredPipe(string pipeName, ScriptGroupReaderIdentity readerIdentity)
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var monitor = WindowsIdentity.GetCurrent().User!;

            // Set explicitly rather than left to default. The owner of a kernel object is
            // taken from the creating token's default owner, which for an account that belongs
            // to Administrators is BUILTIN\Administrators rather than the account itself. The
            // Runner authenticates this pipe by its owner, so the owner has to be the thing the
            // Runner was told to expect, not whatever the token's group membership implies.
            security.SetOwner(monitor);

            security.AddAccessRule(new PipeAccessRule(
                monitor, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            AddRunnerRule(security, monitor, readerIdentity);

            try
            {
                return NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.Out,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    security);
            }
            catch (IOException ex)
            {
                // The name is already held. Under the old code this could not be detected,
                // because the create happened on a background task whose failure nobody
                // observed. It is a refusal to deploy, not a deployment that proceeds against
                // an unknown counterparty.
                throw new InvalidOperationException(
                    $"The script group pipe name '{pipeName}' is already in use on this host. The Monitor" +
                    " will not deploy over a pipe it does not own.", ex);
            }
        }

        /// <summary>
        /// Admits the account the Runner will be started as. Resolution failure is logged
        /// rather than thrown, on the same reasoning as the file transport's grant: this rule
        /// only ever widens access to the pipe, the confinement is the protected access list
        /// that has already been built, and where the Monitor and deployment accounts coincide
        /// — as they do today — the Monitor's own rule already admits the Runner. Throwing
        /// would turn a directory-service blip into a failed production deployment.
        /// </summary>
        private void AddRunnerRule(
            PipeSecurity security,
            SecurityIdentifier monitor,
            ScriptGroupReaderIdentity readerIdentity)
        {
            var account = readerIdentity.QualifiedAccountName;

            try
            {
                var runner = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));

                if (runner.Equals(monitor))
                {
                    return;
                }

                security.AddAccessRule(new PipeAccessRule(runner, RunnerRights, AccessControlType.Allow));
            }
            catch (IdentityNotMappedException ex)
            {
                logger.LogError(
                    ex,
                    "The deployment account '{Account}' could not be admitted to the script group pipe."
                    + " The Runner will be unable to read it unless it runs as an account the pipe already admits.",
                    SanitizeForLog(account));
            }
        }

        private static string SanitizeForLog(string value)
        {
            return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        private void Serve(
            string pipeName,
            NamedPipeServerStream pipeServer,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            using (pipeServer)
            {
                try
                {
                    logger.LogInformation($"Waiting for pipe client to connect. Pipe name: '{pipeName}'.");

                    pipeServer.WaitForConnectionAsync(cancellationToken).GetAwaiter().GetResult();

                    logger.LogInformation($"Pipe client has connected. Pipe name: '{pipeName}'.");

                    var serializeOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters =
                        {
                            new VariableValueJsonConverter(),
                        }
                    };

                    JsonSerializer.SerializeAsync(pipeServer, scriptGroup, serializeOptions, cancellationToken)
                        .GetAwaiter().GetResult();

                    cancellationToken.ThrowIfCancellationRequested();

                    pipeServer.WaitForPipeDrain();

                    logger.LogInformation($"Pipe client has received serialized ScriptGroup. Pipe name: '{pipeName}'.");
                }
                catch (OperationCanceledException)
                {
                    // The deployment was cancelled or the Runner exited without reading. Not a
                    // transport fault, and the caller already knows.
                    logger.LogInformation($"Script group pipe '{pipeName}' was cancelled before it was consumed.");
                }
                catch (Exception e)
                {
                    logger.LogError($"Named pipe has failed. Pipe name: '{pipeName}'. Exception: {e}");
                    throw;
                }
            }
        }
    }
}
