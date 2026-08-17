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
        /// <summary>
        /// How long a bundle may survive the deployment that produced it before a later
        /// deployment removes it. A bundle is read once, within seconds of the Runner
        /// starting, so this is far longer than any legitimate need; it exists only to clear
        /// bundles orphaned by a Monitor that died between writing one and expiring it.
        /// </summary>
        private static readonly TimeSpan OrphanedBundleRetention = TimeSpan.FromDays(1);

        private ILogger logger;

        public ScriptGroupFileWriter(ILogger<ScriptGroupFileWriter> logger)
        {
            this.logger = logger;
        }

        public Task Start(
            string pipeName,
            ScriptGroup scriptGroup,
            ScriptGroupReaderIdentity readerIdentity,
            CancellationToken cancellationToken)
        {
            string filename = BundlePath(pipeName);
            try
            {
                // The serialised ScriptGroup contains secrets (GitHubToken, AzureBearerToken,
                // TerraformGitPat) alongside every resolved deployment property. The directory
                // ACL is locked down to the writing service account + SYSTEM + Administrators,
                // with ContainerInherit | ObjectInherit so newly-created child files inherit
                // the same restriction.
                EnsureRestrictedDirectory(RunnerConstants.ScriptGroupFilesPath);
                RemoveOrphanedBundles(RunnerConstants.ScriptGroupFilesPath);

                // Deleted rather than truncated. File.Create on an existing file keeps that
                // file's discretionary access list, so a bundle left behind by a build that
                // predates the restricted directory - the whole accumulated backlog - would
                // hand its permissive ACL to the bundle written over it, and the directory's
                // restriction would never apply to it. Bundle names repeat across attempts at
                // the same request, so this is reachable, not hypothetical.
                Expire(pipeName);

                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                                {
                                    new VariableValueJsonConverter(),
                                }
                };

                using (FileStream createStream = File.Create(filename))
                {
                    JsonSerializer.Serialize(createStream, scriptGroup, serializeOptions);
                }

                GrantReadAccessToRunner(filename, readerIdentity);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                logger.LogError($"Script group bundle could not be written. File name: '{filename}'. Exception: {ex}");

                // A bundle that was written but not completed must not be left behind for the
                // Runner - or anyone else - to read.
                Expire(pipeName);
                throw;
            }
        }

        /// <summary>
        /// Deletes the bundle. The secrets it carries are needed for the span of one Runner
        /// process; retaining them afterwards turns a transient in-memory exposure into a
        /// durable on-disk one, and every bundle ever written was previously kept.
        /// </summary>
        public void Expire(string pipeName)
        {
            string filename = BundlePath(pipeName);
            try
            {
                if (File.Exists(filename))
                {
                    File.Delete(filename);
                    logger.LogDebug($"Script group bundle '{filename}' has been removed.");
                }
            }
            catch (Exception ex)
            {
                // Called from a finally block on the deployment path. Failing to delete is
                // worth knowing about but must not replace the deployment's own outcome.
                logger.LogError($"Failed to remove script group bundle '{filename}'. Exception: {ex}");
            }
        }

        private static string BundlePath(string pipeName) =>
            $"{RunnerConstants.ScriptGroupFilesPath}{pipeName}.json";

        /// <summary>
        /// Adds the Runner's own principal to the bundle's ACL.
        ///
        /// The directory DACL is protected and names only the Monitor's account, so the file
        /// inherits nothing the Runner can use. Where the Monitor and deployment accounts
        /// coincide this ACE is redundant; where they differ, its absence is what would
        /// otherwise make the bundle unreadable. Access is granted on the file rather than the
        /// directory so that the deployment account can reach the bundle it is meant to consume
        /// and not those of concurrent deployments — the isolation that buys is per-deployment,
        /// but the principal need not be: an environment that names no execution identity of its
        /// own still resolves to one shared account per production/non-production tier, so any
        /// process already running as it can still read that tier's in-flight bundles. What
        /// narrows the principal is the environment naming its own identity; that is a migration
        /// per environment, not a property of this code.
        ///
        /// Reaching a file by path also needs traverse rights on its parents. Those come from
        /// the "Bypass traverse checking" right, which Windows grants to Everyone by default;
        /// a host hardened to withdraw it would need the deployment account added to the
        /// directory ACL as well, and the symptom would be an access denial on open.
        ///
        /// Failure here is logged and swallowed, deliberately. This grant only ever WIDENS
        /// access to the bundle; the confinement is the directory DACL, which has already been
        /// applied by the time this runs. So a failure — a domain controller briefly
        /// unreachable, a Monitor host not joined, an account the local authority cannot
        /// resolve — can leave the bundle unreadable but never leave it over-readable. Throwing
        /// would convert a transient directory-service blip into a failed production
        /// deployment, which is a far worse outcome than the one being guarded against.
        /// </summary>
        private void GrantReadAccessToRunner(string filename, ScriptGroupReaderIdentity readerIdentity)
        {
            var account = readerIdentity.QualifiedAccountName;

            // A misconfigured account name that resolves to a broad group would publish the
            // bundle to it. The deployment will fail moments later when the logon is attempted
            // with the same name, and the bundle is expired on that path - but not granting it
            // in the first place is free.
            if (!readerIdentity.TryResolveSecurityIdentifier(out var reader, out var refusal))
            {
                logger.LogError(
                    $"{refusal} No access to the script group bundle '{filename}' has been granted, so the" +
                    " Runner will be unable to read it unless it runs as an account the directory already" +
                    " admits.");
                return;
            }

            try
            {
                var fileInfo = new FileInfo(filename);
                var security = fileInfo.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    reader,
                    FileSystemRights.Read,
                    AccessControlType.Allow));
                fileInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    $"The deployment account '{account}' could not be granted access to the script group" +
                    $" bundle '{filename}'. The Runner will be unable to read it unless it runs as an" +
                    $" account the directory already admits. Exception: {ex}");
            }
        }

        /// <summary>
        /// Removes bundles that outlived the deployment that wrote them. Expiry on the
        /// deployment path covers the normal and failed cases; this covers the case where the
        /// Monitor process itself did not survive to run it, and clears bundles accumulated
        /// before expiry existed at all.
        /// </summary>
        private void RemoveOrphanedBundles(string path)
        {
            try
            {
                var cutoff = DateTime.UtcNow - OrphanedBundleRetention;
                var removed = 0;

                foreach (var bundle in Directory.EnumerateFiles(path, "*.json"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(bundle) >= cutoff)
                        {
                            continue;
                        }

                        File.Delete(bundle);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Failed to remove orphaned script group bundle '{bundle}'. Exception: {ex}");
                    }
                }

                if (removed > 0)
                {
                    logger.LogInformation($"Removed {removed} orphaned script group bundle(s) from '{path}'.");
                }
            }
            catch (Exception ex)
            {
                // Housekeeping. Never let it stop a deployment.
                logger.LogWarning($"Failed to enumerate script group bundles in '{path}'. Exception: {ex}");
            }
        }

        private static void EnsureRestrictedDirectory(string path)
        {
            var security = BuildRestrictedDirectorySecurity();
            if (!Directory.Exists(path))
            {
                // FileSystemAclExtensions: creates the directory with the supplied DACL atomically,
                // so the secrets folder never exists with the default Users-readable ACL.
                security.CreateDirectory(path);
                return;
            }

            // Re-apply the restricted DACL on every start. If an admin manually deletes and
            // re-creates the folder with default permissions (or restores from backup with
            // looser ACLs), skipping this would leave subsequent secret files readable by
            // any local authenticated user. The reapply is a single SetAccessControl call.
            new DirectoryInfo(path).SetAccessControl(security);
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
            // Only the service account writing the file plus SYSTEM and BUILTIN\Administrators
            // retain access to the directory. Everything else — including authenticated
            // interactive users on the Monitor host — is denied by the absence of an inherited
            // Users ACE. The account that READS a bundle is granted on the bundle itself, by
            // GrantReadAccessToRunner, because it is a per-deployment principal rather than a
            // property of the directory.
            yield return WindowsIdentity.GetCurrent().User!;
            yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        }
    }
}
