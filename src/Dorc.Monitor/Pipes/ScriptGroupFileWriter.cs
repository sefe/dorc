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
        private static readonly object BundleDirectorySync = new();

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
                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                                {
                                    new VariableValueJsonConverter(),
                                }
                };

                var runnerSid = ResolveRunnerIdentity(readerIdentity, filename);
                var bundleSecurity = BuildProtectedBundleSecurity(runnerSid);

                lock (BundleDirectorySync)
                {
                    // The serialised ScriptGroup contains secrets (GitHubToken, AzureBearerToken,
                    // TerraformGitPat) alongside every resolved deployment property. The directory
                    // ACL is locked down to the writing service account + SYSTEM + Administrators,
                    // and each bundle file is created atomically with its final protected DACL
                    // (including the resolved Runner Read ACE) so concurrent orphan sweeps never
                    // need to rewrite a live bundle's ACL.
                    EnsureRestrictedDirectory(RunnerConstants.ScriptGroupFilesPath);
                    RemoveOrphanedBundlesCore(RunnerConstants.ScriptGroupFilesPath);

                    // Deleted rather than truncated so a pre-existing file cannot lend an older
                    // ACL or collide with FileMode.CreateNew. Bundle names repeat across attempts
                    // at the same request, so this is reachable, not hypothetical.
                    ExpireCore(pipeName);

                    using FileStream createStream = FileSystemAclExtensions.Create(
                        new FileInfo(filename),
                        FileMode.CreateNew,
                        FileSystemRights.WriteData
                            | FileSystemRights.AppendData
                            | FileSystemRights.ReadAttributes
                            | FileSystemRights.Synchronize,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.None,
                        bundleSecurity);
                    JsonSerializer.Serialize(createStream, scriptGroup, serializeOptions);
                }

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
            lock (BundleDirectorySync)
            {
                ExpireCore(pipeName);
            }
        }

        private void ExpireCore(string pipeName)
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
        private SecurityIdentifier? ResolveRunnerIdentity(
            ScriptGroupReaderIdentity readerIdentity,
            string filename)
        {
            var resolution = readerIdentity.Resolve();

            if (!resolution.IsResolved)
            {
                // A misconfigured account name that resolves to a broad group would publish
                // the bundle to it, and one that does not resolve at all cannot be granted
                // anything. Either way the deployment will fail moments later when the logon
                // is attempted with the same name, and the bundle is expired on that path.
                logger.LogError(
                    resolution.Cause,
                    "{Refusal} No access has been granted to the script group bundle '{BundlePath}', so the Runner"
                    + " will be unable to read it unless it runs as an account the directory already admits.",
                    resolution.Refusal,
                    filename);
                return null;
            }

            return resolution.SecurityIdentifier;
        }

        private static FileSecurity BuildProtectedBundleSecurity(SecurityIdentifier? runnerSid)
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var privileged in RestrictedDirectory.PrivilegedIdentities().Distinct())
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    privileged,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
            }

            if (runnerSid != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    runnerSid,
                    FileSystemRights.Read,
                    AccessControlType.Allow));
            }

            return security;
        }

        /// <summary>
        /// Removes bundles that outlived the deployment that wrote them, and confines any
        /// younger retained bundles (such as legacy bundles written within the retention window
        /// before the directory was restricted) through verified single-link child handles.
        /// Expiry on the deployment path covers the normal and failed cases; this covers the
        /// case where the Monitor process itself did not survive to run it, and clears or
        /// confines bundles accumulated before expiry and directory restriction existed.
        /// </summary>
        internal void RemoveOrphanedBundles(string path)
        {
            lock (BundleDirectorySync)
            {
                RemoveOrphanedBundlesCore(path);
            }
        }

        private void RemoveOrphanedBundlesCore(string path)
        {
            try
            {
                using var dirHandle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                    path,
                    ReparseFreeHandles.FileReadAttributes);
                var canonicalRoot = ReparseFreeHandles.GetCanonicalPath(dirHandle, path);
                var privilegedIdentities = RestrictedDirectory.PrivilegedIdentities().Distinct().ToArray();
                var privilegedSet = privilegedIdentities.ToHashSet();

                var cutoff = DateTime.UtcNow - OrphanedBundleRetention;
                var removed = 0;

                foreach (var bundle in Directory.EnumerateFiles(path, "*.json"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(bundle) >= cutoff)
                        {
                            ConfineRetainedBundle(bundle, canonicalRoot, privilegedIdentities, privilegedSet);
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

        private static void ConfineRetainedBundle(
            string bundlePath,
            string canonicalRoot,
            IReadOnlyList<SecurityIdentifier> privilegedIdentities,
            HashSet<SecurityIdentifier> privilegedSet)
        {
            var leafName = Path.GetFileName(bundlePath);
            if (string.IsNullOrEmpty(leafName))
            {
                return;
            }

            var expectedCanonicalFilePath = Path.Join(canonicalRoot, leafName);
            using var fileHandle = ReparseFreeHandles.OpenVerifiedRegularFileForDacl(
                bundlePath,
                expectedCanonicalFilePath);
            var currentSecurity = ReparseFreeHandles.GetFileSecurity(fileHandle, bundlePath);
            var existingRules = currentSecurity
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();

            var needsConfinement = !currentSecurity.AreAccessRulesProtected
                || existingRules.Any(rule =>
                    rule.IsInherited
                    || (rule.AccessControlType == AccessControlType.Allow
                        && !privilegedSet.Contains((SecurityIdentifier)rule.IdentityReference)
                        && (DeploymentPrincipal.IsTooBroadToHoldASecret((SecurityIdentifier)rule.IdentityReference)
                            || (rule.FileSystemRights & ~(FileSystemRights.Read | FileSystemRights.Synchronize)) != 0)));
            if (!needsConfinement)
            {
                return;
            }

            var hardenedSecurity = new FileSecurity();
            hardenedSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var privileged in privilegedIdentities)
            {
                hardenedSecurity.AddAccessRule(new FileSystemAccessRule(
                    privileged,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
            }

            // Preserve any explicit (non-inherited) per-deployment Runner Read grant on an
            // in-flight bundle while stripping all inherited or broad principals.
            foreach (var rule in existingRules)
            {
                if (rule.IsInherited || rule.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var sid = (SecurityIdentifier)rule.IdentityReference;
                if (privilegedSet.Contains(sid) || DeploymentPrincipal.IsTooBroadToHoldASecret(sid))
                {
                    continue;
                }

                if ((rule.FileSystemRights & ~(FileSystemRights.Read | FileSystemRights.Synchronize)) == 0)
                {
                    hardenedSecurity.AddAccessRule(new FileSystemAccessRule(
                        sid,
                        FileSystemRights.Read,
                        AccessControlType.Allow));
                }
            }

            ReparseFreeHandles.ApplyProtectedDacl(fileHandle, hardenedSecurity, bundlePath);
        }

        private void LogOrphanNotRemoved(Exception ex, string bundle) =>
            logger.LogWarning(ex, "Failed to remove orphaned script group bundle '{BundlePath}'.", bundle);

        private void LogSweepAbandoned(Exception ex, string path) =>
            logger.LogWarning(ex, "Failed to enumerate script group bundles in '{BundleDirectory}'.", path);

        private static void EnsureRestrictedDirectory(string path) => RestrictedDirectory.Ensure(path);
    }
}
