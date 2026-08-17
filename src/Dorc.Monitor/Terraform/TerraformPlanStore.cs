using Dorc.Monitor.Pipes;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Terraform
{
    /// <summary>
    /// The local home of one deployment's Terraform plan artefacts, for as long as they are
    /// needed and no longer.
    ///
    /// Two artefacts land here: the binary plan, which embeds the variable values it was planned
    /// with, and the rendered plan content, which lists them in plain text. Both were previously
    /// written straight into a single shared <c>%ProgramData%\dorc\terraform-plans</c> directory
    /// created with inherited permissions — readable by any authenticated user on the deployment
    /// host — and neither was ever removed. Every plan of every deployment the host had ever run
    /// accumulated there.
    ///
    /// Restricting a shared directory is not possible without knowing who to admit, which is why
    /// this could not be done alongside the other artefact hardening: the deployment identity was
    /// a production boolean at the time. It is now the environment's own, so the layout changes
    /// with it — one directory per deployment result, its access control list protected from
    /// inheritance and naming that deployment's identity alone. A concurrent deployment running
    /// under a different environment's identity cannot read this one's plan.
    ///
    /// The directory rather than the files must admit the deployment identity, because the plan
    /// files do not exist yet: <c>terraform plan -out</c> creates the binary plan and the Runner
    /// writes the rendered content, both as the deployment account. That is the difference from
    /// the script-group bundle, which the Monitor writes and grants on the file.
    /// </summary>
    internal sealed class TerraformPlanStore
    {
        /// <summary>
        /// How long plan artefacts left behind by an earlier deployment may survive before a
        /// later one removes them.
        ///
        /// Longer than the bundle's retention, deliberately. A bundle is read once within seconds;
        /// a plan is the durable half of a human-gated handshake, and while the uploaded blob is
        /// what the apply reads back, a local survivor means the upload did not happen. That is
        /// the one copy an operator has, so it is kept long enough to be noticed rather than
        /// swept away by the next deployment on the host.
        /// </summary>
        private static readonly TimeSpan OrphanedPlanRetention = TimeSpan.FromDays(7);

        private readonly ILogger _logger;

        private TerraformPlanStore(ILogger logger, string directory)
        {
            _logger = logger;
            Directory = directory;
        }

        /// <summary>The per-deployment directory the plan artefacts live in.</summary>
        public string Directory { get; }

        /// <summary>
        /// Prepares this deployment's plan directory and returns a store over it.
        /// </summary>
        /// <param name="root">
        /// The shared plan root, normally <c>%ProgramData%\dorc\terraform-plans</c>. Injected so
        /// that the layout and lifetime are exercisable without writing under ProgramData.
        /// </param>
        /// <param name="deploymentResultId">
        /// Names the directory. The same identifier is used for the plan and for the apply that
        /// follows it, so both operations of one handshake share a directory — which is what makes
        /// the apply able to find the plan it downloads.
        /// </param>
        /// <param name="deploymentIdentity">
        /// The account the Runner will be started as, carried from the credential resolution
        /// point rather than assumed to be the Monitor's own.
        /// </param>
        public static TerraformPlanStore Reserve(
            ILogger logger,
            string root,
            int deploymentResultId,
            ScriptGroupReaderIdentity deploymentIdentity)
        {
            var directory = Path.Join(
                root,
                deploymentResultId.ToString(CultureInfo.InvariantCulture));

            var store = new TerraformPlanStore(logger, directory);

            store.EnsureRoot(root);
            store.RemoveOrphanedPlans(root);
            store.EnsureDeploymentDirectory(deploymentIdentity);

            return store;
        }

        /// <summary>
        /// Where a named plan artefact belongs. Plan file names are derived from the deployment
        /// result identifier and are the blob names too, so they are not attacker-chosen — but a
        /// name that escaped this directory would escape its access control list as well, so the
        /// join refuses anything that is not a bare file name.
        /// </summary>
        public string PathOf(string planFileName)
        {
            if (string.IsNullOrWhiteSpace(planFileName)
                || planFileName != Path.GetFileName(planFileName))
            {
                throw new ArgumentException(
                    "A Terraform plan artefact must be named by a bare file name.",
                    nameof(planFileName));
            }

            return Path.Combine(Directory, planFileName);
        }

        /// <summary>
        /// Removes this deployment's plan artefacts.
        ///
        /// Called once the artefacts have a durable copy elsewhere — after the plan blobs are
        /// uploaded, or after an apply that read its plan back from one. Deliberately NOT called
        /// on the failure path of a plan: if the upload is what failed, the local copy is the only
        /// copy there is, and deleting it would turn a recoverable failure into a lost plan.
        /// </summary>
        public void Expire()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                    _logger.LogDebug($"Local Terraform plan artefacts in '{Directory}' have been removed.");
                }
            }
            catch (Exception ex)
            {
                // Called from the deployment path. Failing to delete is worth knowing about but
                // must not replace the deployment's own outcome.
                _logger.LogError(
                    $"Failed to remove local Terraform plan artefacts in '{Directory}'. Exception: {ex}");
            }
        }

        private void EnsureRoot(string root)
        {
            if (!OperatingSystem.IsWindows())
            {
                System.IO.Directory.CreateDirectory(root);
                return;
            }

            EnsureRestrictedRoot(root);
        }

        private void EnsureDeploymentDirectory(ScriptGroupReaderIdentity deploymentIdentity)
        {
            if (!OperatingSystem.IsWindows())
            {
                // DOrc deploys on Windows; this branch exists so the layout and lifetime are
                // exercisable off it. No restriction is applied here, so it must never be the
                // production path.
                System.IO.Directory.CreateDirectory(Directory);
                return;
            }

            CreateRestrictedDeploymentDirectory(deploymentIdentity);
        }

        [SupportedOSPlatform("windows")]
        private void EnsureRestrictedRoot(string root)
        {
            var security = RestrictedSecurity(admitting: null);

            if (!System.IO.Directory.Exists(root))
            {
                // Created with the supplied access control list atomically, so the root never
                // exists with the permissions it would have inherited from ProgramData.
                security.CreateDirectory(root);
                return;
            }

            // Re-asserted on every deployment, as the bundle directory is. The root predates this
            // change on every existing host, so the first deployment after the release is what
            // corrects the permissions it was created with.
            new DirectoryInfo(root).SetAccessControl(security);
        }

        [SupportedOSPlatform("windows")]
        private void CreateRestrictedDeploymentDirectory(ScriptGroupReaderIdentity deploymentIdentity)
        {
            // Failure to resolve the identity is logged, not thrown. The confinement is the
            // protected access control list, which is applied either way; what an unresolvable
            // identity costs is the deployment's ability to write its plan, and that surfaces as
            // an access denial from Terraform moments later. Throwing here would turn a transient
            // directory-service blip into a failed production deployment, and would do it before
            // the deployment had any chance to report why.
            SecurityIdentifier? deploymentAccount = null;
            if (!deploymentIdentity.TryResolveSecurityIdentifier(out deploymentAccount, out var refusal))
            {
                _logger.LogError(
                    $"{refusal} The Terraform plan directory '{Directory}' has been created without it, so"
                    + " the Runner will be unable to write its plan unless it runs as an account the"
                    + " directory already admits.");
            }

            var security = RestrictedSecurity(deploymentAccount);

            if (!System.IO.Directory.Exists(Directory))
            {
                security.CreateDirectory(Directory);
                return;
            }

            // A surviving directory belongs to an earlier attempt at the same deployment result,
            // or to the plan half of this handshake. Its contents are left alone — they may be the
            // only copy of a plan whose upload failed — but the access control list is re-applied,
            // because the identity admitted may have changed since it was created.
            //
            // Overwriting a file preserves that file's own access control list, which would matter
            // if a permissively-created file could end up in here. None can: this directory layout
            // is introduced by this change, so nothing inside one was created before the
            // restriction existed.
            new DirectoryInfo(Directory).SetAccessControl(security);
        }

        [SupportedOSPlatform("windows")]
        private static DirectorySecurity RestrictedSecurity(SecurityIdentifier? admitting)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (var identity in PrivilegedIdentities(admitting))
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            return security;
        }

        [SupportedOSPlatform("windows")]
        private static IEnumerable<IdentityReference> PrivilegedIdentities(SecurityIdentifier? admitting)
        {
            // The Monitor writes here on the apply path, downloading the plan blob for the Runner
            // to read, so its own identity is needed on both the root and the per-deployment
            // directory. SYSTEM and Administrators keep the host administrable.
            yield return WindowsIdentity.GetCurrent().User!;
            yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            if (admitting != null)
            {
                // Only on the per-deployment directory. Admitting the deployment identity to the
                // root would let it enumerate — and read — every other deployment's plan, which is
                // the disclosure this layout exists to prevent.
                yield return admitting;
            }
        }

        /// <summary>
        /// Removes plan artefacts that outlived the deployment that produced them.
        ///
        /// Expiry on the deployment path covers the artefacts of a deployment that reached its
        /// upload; this covers the ones whose Monitor did not survive to run it, the ones whose
        /// upload failed, and — importantly — the entire backlog accumulated before any of this
        /// existed, which on a long-lived deployment host is every plan it has ever run.
        ///
        /// That backlog sits directly in the root rather than in a per-deployment directory, so
        /// both shapes are swept.
        /// </summary>
        private void RemoveOrphanedPlans(string root)
        {
            try
            {
                var cutoff = DateTime.UtcNow - OrphanedPlanRetention;
                var removed = 0;

                foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(root))
                {
                    try
                    {
                        // Never the directory this deployment is about to use, whatever its age
                        // says. An apply confirmed after the retention window would otherwise
                        // sweep the plan it is running.
                        if (string.Equals(
                                Path.GetFullPath(entry),
                                Path.GetFullPath(Directory),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (System.IO.Directory.Exists(entry))
                        {
                            // The write time of a directory does not move when a file inside it is
                            // written, so the newest artefact decides whether the directory is
                            // spent - not the moment it was created.
                            if (LastWriteWithin(entry, cutoff))
                            {
                                continue;
                            }

                            System.IO.Directory.Delete(entry, recursive: true);
                        }
                        else
                        {
                            if (File.GetLastWriteTimeUtc(entry) >= cutoff)
                            {
                                continue;
                            }

                            File.Delete(entry);
                        }

                        removed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            $"Failed to remove orphaned Terraform plan artefacts at '{entry}'. Exception: {ex}");
                    }
                }

                if (removed > 0)
                {
                    _logger.LogInformation(
                        $"Removed {removed} orphaned Terraform plan artefact(s) from '{root}'.");
                }
            }
            catch (Exception ex)
            {
                // Housekeeping. Never let it stop a deployment.
                _logger.LogWarning(
                    $"Failed to enumerate Terraform plan artefacts in '{root}'. Exception: {ex}");
            }
        }

        private static bool LastWriteWithin(string directory, DateTime cutoff)
        {
            if (System.IO.Directory.GetLastWriteTimeUtc(directory) >= cutoff)
            {
                return true;
            }

            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(
                directory, "*", SearchOption.AllDirectories))
            {
                var lastWrite = System.IO.Directory.Exists(entry)
                    ? System.IO.Directory.GetLastWriteTimeUtc(entry)
                    : File.GetLastWriteTimeUtc(entry);

                if (lastWrite >= cutoff)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
