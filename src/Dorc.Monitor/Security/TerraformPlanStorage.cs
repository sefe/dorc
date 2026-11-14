using Dorc.Monitor.Pipes;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.Versioning;
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
    /// The shared root is restricted to the Monitor, SYSTEM and Administrators alone, while
    /// each deployment result stages into its own subdirectory that additionally admits only
    /// that deployment's Runner account - so concurrent environments never overwrite one
    /// another's directory DACL or expose plans across execution identities.
    /// </summary>
    internal static class TerraformPlanStorage
    {
        internal const string DirectoryName = "terraform-plans";

        /// <summary>
        /// Returns the per-result plan directory, restricted to the Monitor, SYSTEM,
        /// Administrators and the deployment account the Runner will be started as, beneath a
        /// root directory restricted to the Monitor, SYSTEM and Administrators alone.
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
            int deploymentResultId,
            ScriptGroupReaderIdentity readerIdentity,
            ILogger logger)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(deploymentResultId);

            var planStorageRoot = Path.Join(dorcProgramDataRoot, DirectoryName);
            var resultDir = Path.Join(
                planStorageRoot,
                deploymentResultId.ToString(CultureInfo.InvariantCulture));

            if (!OperatingSystem.IsWindows())
            {
                // DOrc deploys on Windows; this branch exists so the surrounding logic is
                // exercisable off it. No restriction is applied here, so it must never be the
                // production path.
                Directory.CreateDirectory(resultDir);
                return resultDir;
            }

            Restrict(planStorageRoot, resultDir, readerIdentity, logger);
            return resultDir;
        }

        [SupportedOSPlatform("windows")]
        private static void Restrict(
            string planStorageRoot,
            string resultDir,
            ScriptGroupReaderIdentity readerIdentity,
            ILogger logger)
        {
            RestrictedDirectory.Ensure(planStorageRoot);

            var deploymentAccount = ResolveDeploymentAccount(readerIdentity, logger);

            RestrictedDirectory.Ensure(
                resultDir,
                deploymentAccount is null ? [] : [deploymentAccount]);
        }

        [SupportedOSPlatform("windows")]
        private static SecurityIdentifier? ResolveDeploymentAccount(
            ScriptGroupReaderIdentity readerIdentity,
            ILogger logger)
        {
            var resolution = readerIdentity.Resolve();

            if (resolution.IsResolved)
            {
                return resolution.SecurityIdentifier;
            }

            logger.LogError(
                resolution.Cause,
                "{Refusal} It has not been admitted to the Terraform plan directory, so the plan will fail to"
                + " be written unless the Runner runs as an account the directory already admits.",
                resolution.Refusal);

            return null;
        }
    }
}
