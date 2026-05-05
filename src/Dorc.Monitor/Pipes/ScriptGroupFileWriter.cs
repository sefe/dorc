using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Dorc.ApiModel.MonitorRunnerApi;
using AccessControlType = System.Security.AccessControl.AccessControlType;

namespace Dorc.Monitor.Pipes
{
    [SupportedOSPlatform("windows")]
    internal class ScriptGroupFileWriter : IScriptGroupPipeServer
    {
        private ILogger logger;

        public ScriptGroupFileWriter(ILogger<ScriptGroupFileWriter> logger)
        {
            this.logger = logger;
        }

        public Task Start(string pipeName, ScriptGroup scriptGroup, CancellationToken cancellationToken)
        {
            string filesPath = RunnerConstants.ScriptGroupFilesPath;
            string filename = $"{filesPath}{pipeName}.json";
            try
            {
                // The serialised ScriptGroup contains secrets (GitHubToken, AzureBearerToken,
                // TerraformGitPat). The directory ACL is locked down to the writing service
                // account + SYSTEM + Administrators, with ContainerInherit | ObjectInherit so
                // newly-created child files inherit the same restriction.
                EnsureRestrictedDirectory(filesPath);

                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                                {
                                    new VariableValueJsonConverter(),
                                }
                };

                using FileStream createStream = File.Create(filename);

                JsonSerializer.Serialize(createStream, scriptGroup, serializeOptions);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                logger.LogError($"File creation has failed. File name: '{filename}'. Exception: {ex}");
                throw;
            }
        }

        private static void EnsureRestrictedDirectory(string path)
        {
            if (Directory.Exists(path))
                return;
            // FileSystemAclExtensions: creates the directory with the supplied DACL atomically,
            // so the secrets folder never exists with the default Users-readable ACL.
            BuildRestrictedDirectorySecurity().CreateDirectory(path);
        }

        private static DirectorySecurity BuildRestrictedDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in PrivilegedIdentities())
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
            return security;
        }

        private static IEnumerable<IdentityReference> PrivilegedIdentities()
        {
            // Only the service account writing the file (typically the same account the Runner
            // executes under) plus SYSTEM and BUILTIN\Administrators retain access. Everything
            // else — including authenticated interactive users on the Monitor host — is denied
            // by the absence of an inherited Users ACE.
            yield return WindowsIdentity.GetCurrent().User!;
            yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        }
    }
}
