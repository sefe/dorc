using Dorc.ApiModel;
using Microsoft.Extensions.Configuration;

namespace Dorc.PersistentData.Security
{
    /// <summary>
    /// Decides which configuration values are published into deployment script scope.
    ///
    /// Every secure configuration value is decrypted and injected into every deployment whose
    /// production flag matches, with no exclusion for the credentials DOrc needs in order to
    /// operate at all. The production deployment password among them: an estate inventory found
    /// it reaching all 1,083 non-production environments, which is the lowest-trust population
    /// there is and the easiest place to land a component that reads it.
    ///
    /// The estate's secure keys are two classes, not one, and this is the whole difficulty of
    /// the step. A completed scan of the production script share found:
    ///
    /// - Operator-only, zero consumers: DORC_ProdDeployPassword, DORC_WebDeployPassword,
    ///   DorcApiAccessPassword. Withholding these breaks nothing.
    /// - Deliberately script-facing: DeploymentServiceAccountPassword (~80 call sites, and the
    ///   mechanism by which scripts reach target servers at all), ProgetAccountPassword (~12),
    ///   DorcCliSecret (2). Withholding these breaks the estate.
    ///
    /// So the withheld set is a configured list defaulting to the three with no consumers,
    /// rather than "every secure value". The remainder wait on their consumers being migrated;
    /// what is still published is reported so that backlog stays visible rather than assumed.
    ///
    /// Usernames stay visible deliberately. Scripts legitimately consume them to grant logon
    /// rights, and an account name is an identity rather than a credential — already visible in
    /// target-server access control lists, process listings and event logs.
    /// </summary>
    public interface IScriptScopeConfigValues
    {
        /// <summary>
        /// Whether this key must not reach deployment script scope.
        /// </summary>
        bool IsWithheld(string? key);

        /// <summary>
        /// The keys being withheld, for reporting.
        /// </summary>
        IReadOnlyCollection<string> WithheldKeys { get; }

        /// <summary>
        /// Secure keys that are still published — the migration backlog. Derived from what is
        /// actually in the estate rather than from a list in code, so a secure value added
        /// after this was written is reported rather than silently overlooked.
        /// </summary>
        IReadOnlyCollection<string> StillPublishedSecureKeys(IEnumerable<ConfigValueApiModel> configValues);
    }

    public class ScriptScopeConfigValues : IScriptScopeConfigValues
    {
        public const string WithheldKeysSetting = "AppSettings:ConfigKeysWithheldFromScripts";

        /// <summary>
        /// The keys a completed production share scan showed no script consumes. Defaulted
        /// rather than left empty because an empty default would leave a live incident open on
        /// every estate that had not yet configured the setting, and defaulted rather than
        /// derived from "is it secure" because deriving it would withhold the three keys the
        /// estate genuinely depends on and break roughly ninety call sites.
        /// </summary>
        private static readonly string[] OperatorOnlyKeys =
        {
            "DORC_ProdDeployPassword",
            "DORC_WebDeployPassword",
            "DorcApiAccessPassword"
        };

        private readonly HashSet<string> withheld;

        public ScriptScopeConfigValues(IConfiguration configuration)
        {
            var configured = configuration.GetSection(WithheldKeysSetting).Get<string[]>();

            withheld = new HashSet<string>(
                (configured ?? OperatorOnlyKeys).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<string> WithheldKeys => withheld;

        public bool IsWithheld(string? key) =>
            !string.IsNullOrWhiteSpace(key) && withheld.Contains(key.Trim());

        public IReadOnlyCollection<string> StillPublishedSecureKeys(IEnumerable<ConfigValueApiModel> configValues)
        {
            if (configValues == null)
            {
                return Array.Empty<string>();
            }

            return configValues
                .Where(value => value != null && value.Secure && !IsWithheld(value.Key))
                .Select(value => value.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
