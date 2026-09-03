using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Dorc.Monitor.Security;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Security;
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

                GrantReadAccessToRunner(pipeName, readerIdentity);

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
            string filename;
            try
            {
                filename = BundlePath(pipeName);
            }
            catch (ArgumentException ex)
            {
                logger.LogError(ex, "A script group bundle that does not name a file in the bundle directory cannot be expired.");
                return;
            }

            try
            {
                if (File.Exists(filename))
                {
                    File.Delete(filename);
                    logger.LogDebug("Script group bundle '{BundlePath}' has been removed.", filename);
                }
            }
            // Called from a finally block on the deployment path. Failing to delete is worth
            // knowing about but must not replace the deployment's own outcome.
            catch (IOException ex) { LogFailedRemoval(ex, filename); }
            catch (UnauthorizedAccessException ex) { LogFailedRemoval(ex, filename); }
            catch (SecurityException ex) { LogFailedRemoval(ex, filename); }
            catch (NotSupportedException ex) { LogFailedRemoval(ex, filename); }
        }

        private void LogFailedRemoval(Exception ex, string filename) =>
            logger.LogError(ex, "Failed to remove script group bundle '{BundlePath}'.", filename);

        /// <summary>
        /// Resolves the bundle path, and refuses anything that is not a plain file name
        /// directly inside the bundle directory.
        ///
        /// The pipe name is composed by the dispatchers from <c>HostInstanceId</c> and a
        /// request id, and <c>HostInstanceId</c> can be set from the environment
        /// (<c>DORC_REPLICA_ID</c>), so it is not a literal. Nothing that reaches here today
        /// carries a separator - but the value is written to, and DELETED from, a directory
        /// holding deployment secrets, and "the caller happens to be trusted" is not a
        /// property this method can check. So it checks the only thing it can: that the name
        /// names one file, in the directory it is supposed to name it in.
        ///
        /// Both halves are needed. Rejecting the invalid characters catches the separators and
        /// the traversal segments at the input; comparing the fully-resolved path against the
        /// fully-resolved root catches whatever the file system would still have re-interpreted
        /// afterwards - a trailing dot or space, an 8.3 alias, a reparse point in the root
        /// itself.
        /// </summary>
        private static string BundlePath(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName)
                || pipeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || pipeName.Contains("..", StringComparison.Ordinal)
                || Path.GetFileName(pipeName) != pipeName)
            {
                throw new ArgumentException(
                    "A script group bundle name must be a single file name within the bundle directory.",
                    nameof(pipeName));
            }

            var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(RunnerConstants.ScriptGroupFilesPath));
            var bundle = Path.GetFullPath(Path.Join(root, pipeName + ".json"));

            if (Path.GetDirectoryName(bundle) != root)
            {
                throw new ArgumentException(
                    $"A script group bundle must resolve inside '{root}'.",
                    nameof(pipeName));
            }

            return bundle;
        }

        /// <summary>
        /// Adds the Runner's own principal to the bundle's ACL.
        ///
        /// The directory DACL is protected and names only the Monitor's account, so the file
        /// inherits nothing the Runner can use. Where the Monitor and deployment accounts
        /// coincide this ACE is redundant; where they differ, its absence is what would
        /// otherwise make the bundle unreadable. Access is granted on the file rather than the
        /// directory so that the deployment account can reach the bundle it is meant to consume
        /// and not those of concurrent deployments — the isolation that buys is per-deployment,
        /// but the principal is not: it is one shared account per production/non-production
        /// tier, so any process already running as it can still read that tier's in-flight
        /// bundles. Narrowing the principal itself is S-021's job, not this one's.
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
        private void GrantReadAccessToRunner(string pipeName, ScriptGroupReaderIdentity readerIdentity)
        {
            var account = readerIdentity.QualifiedAccountName;

            // Resolved here rather than taken as a path, so the confinement check runs on the
            // way to every operation performed on the bundle rather than only on the way to
            // the first one.
            var filename = BundlePath(pipeName);

            try
            {
                var reader = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));

                if (DeploymentPrincipal.IsTooBroadToHoldASecret(reader))
                {
                    // A misconfigured account name that resolves to a broad group would
                    // publish the bundle to it. The deployment will fail moments later when
                    // the logon is attempted with the same name, and the bundle is expired on
                    // that path - but not granting it in the first place is free.
                    logger.LogError(
                        "The configured deployment account '{Account}' resolves to '{Sid}', which is a group" +
                        " broad enough that granting it access to the script group bundle would disclose it." +
                        " No access has been granted.",
                        DeploymentPrincipal.SanitizeForLog(account),
                        reader.Value);
                    return;
                }

                var fileInfo = new FileInfo(filename);
                var security = fileInfo.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    reader,
                    FileSystemRights.Read,
                    AccessControlType.Allow));
                fileInfo.SetAccessControl(security);
            }
            catch (PrivilegeNotHeldException ex) { LogFailedGrant(ex, account, filename); }
            catch (UnauthorizedAccessException ex) { LogFailedGrant(ex, account, filename); }
            catch (IdentityNotMappedException ex) { LogFailedGrant(ex, account, filename); }
            catch (SecurityException ex) { LogFailedGrant(ex, account, filename); }
            catch (IOException ex) { LogFailedGrant(ex, account, filename); }
            catch (SystemException ex) when (ex is not OutOfMemoryException)
            {
                // NTAccount.Translate surfaces directory-service failures as SystemException
                // itself - a plain 'new SystemException' from the SID lookup - so the typed
                // catches above do not cover the case this method exists to survive: an
                // unreachable domain controller must not fail a deployment.
                LogFailedGrant(ex, account, filename);
            }
        }

        private void LogFailedGrant(Exception ex, string account, string filename) =>
            logger.LogError(
                ex,
                "The deployment account '{Account}' could not be granted access to the script group bundle" +
                " '{BundlePath}'. The Runner will be unable to read it unless it runs as an account the" +
                " directory already admits.",
                DeploymentPrincipal.SanitizeForLog(account),
                filename);

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
                    catch (UnauthorizedAccessException ex) { LogOrphanNotRemoved(ex, bundle); }
                    catch (SecurityException ex) { LogOrphanNotRemoved(ex, bundle); }
                    catch (NotSupportedException ex) { LogOrphanNotRemoved(ex, bundle); }
                    catch (IOException ex) { LogOrphanNotRemoved(ex, bundle); }
                }

                if (removed > 0)
                {
                    logger.LogInformation(
                        "Removed {RemovedCount} orphaned script group bundle(s) from '{BundleDirectory}'.",
                        removed,
                        path);
                }
            }
            // Housekeeping. Never let it stop a deployment.
            catch (UnauthorizedAccessException ex) { LogSweepAbandoned(ex, path); }
            catch (SecurityException ex) { LogSweepAbandoned(ex, path); }
            catch (IOException ex) { LogSweepAbandoned(ex, path); }
        }

        private void LogOrphanNotRemoved(Exception ex, string bundle) =>
            logger.LogWarning(ex, "Failed to remove orphaned script group bundle '{BundlePath}'.", bundle);

        private void LogSweepAbandoned(Exception ex, string path) =>
            logger.LogWarning(ex, "Failed to enumerate script group bundles in '{BundleDirectory}'.", path);

        private static void EnsureRestrictedDirectory(string path) => RestrictedDirectory.Ensure(path);
    }
}
