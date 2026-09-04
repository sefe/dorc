using Dorc.Monitor.Pipes;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Security
{
    /// <summary>
    /// The directory the Monitor stages Terraform plans in, and the Runner writes them into.
    ///
    /// A plan is not an inert artefact. The binary plan file embeds the variable values the
    /// configuration was rendered with - which are resolved deployment properties, secrets
    /// among them - and the human-readable rendering beside it spells out the same values in
    /// clear. Both were created under %ProgramData% with inherited permissions and kept
    /// indefinitely, so any authenticated user on the Monitor host could read every plan the
    /// host had ever staged.
    ///
    /// The directory is restricted the same way the Terraform working directory is, with one
    /// addition: the Runner that writes the plan runs as the deployment account, not as the
    /// Monitor, so that account must be admitted or nothing can be planned at all.
    /// </summary>
    internal static class TerraformPlanStorage
    {
        internal const string DirectoryName = "terraform-plans";

        /// <summary>
        /// Returns the plan directory, restricted to the Monitor, SYSTEM, Administrators and
        /// the deployment account the Runner will be started as.
        ///
        /// A failure to APPLY the restriction is fatal to the dispatch, deliberately: the
        /// alternative is staging a plan in a directory the whole host can read, which is the
        /// exposure this exists to close. A failure to RESOLVE the deployment account is not -
        /// that grant only ever widens access, so its absence can make a plan unwritable but
        /// never over-readable, and the deployment fails moments later on the logon that uses
        /// the same name.
        /// </summary>
        public static string EnsureRestricted(
            string dorcProgramDataRoot,
            ScriptGroupReaderIdentity readerIdentity,
            ILogger logger)
        {
            var planStorageDir = Path.Join(dorcProgramDataRoot, DirectoryName);

            if (!OperatingSystem.IsWindows())
            {
                // DOrc deploys on Windows; this branch exists so the surrounding logic is
                // exercisable off it. No restriction is applied here, so it must never be the
                // production path.
                Directory.CreateDirectory(planStorageDir);
                return planStorageDir;
            }

            Restrict(planStorageDir, readerIdentity, logger);
            return planStorageDir;
        }

        [SupportedOSPlatform("windows")]
        private static void Restrict(
            string planStorageDir,
            ScriptGroupReaderIdentity readerIdentity,
            ILogger logger)
        {
            var deploymentAccount = ResolveDeploymentAccount(readerIdentity, logger);

            RestrictedDirectory.Ensure(
                planStorageDir,
                deploymentAccount is null ? [] : [deploymentAccount]);
        }

        [SupportedOSPlatform("windows")]
        private static SecurityIdentifier? ResolveDeploymentAccount(
            ScriptGroupReaderIdentity readerIdentity,
            ILogger logger)
        {
            var account = readerIdentity.QualifiedAccountName;

            try
            {
                var sid = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));

                if (DeploymentPrincipal.IsTooBroadToHoldASecret(sid))
                {
                    logger.LogError(
                        "The configured deployment account '{Account}' resolves to '{Sid}', which is a group" +
                        " broad enough that admitting it to the Terraform plan directory would disclose every" +
                        " plan staged on this host. It has not been admitted.",
                        DeploymentPrincipal.SanitizeForLog(account),
                        sid.Value);
                    return null;
                }

                return sid;
            }
            catch (IdentityNotMappedException ex) { LogUnresolved(ex, account, logger); }
            catch (SecurityException ex) { LogUnresolved(ex, account, logger); }
            catch (SystemException ex) when (ex is not OutOfMemoryException)
            {
                // NTAccount.Translate reports a directory-service failure as a plain
                // SystemException. An unreachable domain controller must leave the directory
                // restricted, not leave the deployment unable to reach this point at all.
                LogUnresolved(ex, account, logger);
            }

            return null;
        }

        private static void LogUnresolved(Exception ex, string account, ILogger logger) =>
            logger.LogError(
                ex,
                "The deployment account '{Account}' could not be resolved, so it has not been admitted to the" +
                " Terraform plan directory. The plan will fail to be written unless the Runner runs as an" +
                " account the directory already admits.",
                DeploymentPrincipal.SanitizeForLog(account));
    }
}
