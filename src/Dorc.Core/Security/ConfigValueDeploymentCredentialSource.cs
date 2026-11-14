using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security;

namespace Dorc.Core.Security
{
    /// <summary>
    /// Resolves the deployment credential from DOrc's own configuration values — what all four
    /// resolution sites did independently before this existed.
    ///
    /// This remains the default so that introducing the abstraction changes nothing about where
    /// credentials come from. The vault-backed source is the migration target; switching is a
    /// configuration change rather than a code change, which is the point.
    /// </summary>
    public class ConfigValueDeploymentCredentialSource : DeploymentCredentialSource
    {
        internal const string ProductionUserNameKey = "DORC_ProdDeployUsername";
        internal const string ProductionVerificationEntryKey = "DORC_ProdDeployPassword";
        internal const string NonProductionUserNameKey = "DORC_NonProdDeployUsername";
        internal const string NonProductionVerificationEntryKey = "DORC_NonProdDeployPassword";

        private readonly IConfigValuesPersistentSource _configValues;

        public ConfigValueDeploymentCredentialSource(
            IConfigValuesPersistentSource configValues,
            ILogger<ConfigValueDeploymentCredentialSource> logger)
            : base(logger)
        {
            _configValues = configValues;
        }

        public override string Description => "DOrc configuration values";

        /// <summary>
        /// A named identity maps to its own pair of configuration keys,
        /// <c>DORC_Deploy_&lt;identity&gt;_Username</c> and <c>..._Password</c>. Per-identity
        /// keys rather than a fixed set is exactly why the reserved-key denylist could not cover
        /// this variant on its own - a fixed list cannot name keys minted per environment - and
        /// why the per-value classification had to exist first.
        /// </summary>
        protected override DeploymentCredential? ResolveNamedIdentity(string identityReference, DeploymentTier tier)
        {
            var userNameKey = $"DORC_Deploy_{identityReference}_Username";
            var verificationEntryKey = $"DORC_Deploy_{identityReference}_Password";

            var hasSingleUserName = TryReadSingleConfigValue(userNameKey, "username", out var rawUserNameValue);
            var hasSingleVerification = TryReadSingleConfigValue(verificationEntryKey, "verification", out var rawVerificationValue);
            if (!hasSingleUserName || !hasSingleVerification)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(rawUserNameValue) || string.IsNullOrWhiteSpace(rawVerificationValue))
            {
                return null;
            }

            return DeploymentCredential.FromPlainText(rawUserNameValue.Trim(), rawVerificationValue);
        }

        protected override DeploymentCredential? ResolveTierDefault(DeploymentTier tier)
        {
            var userNameKey = tier == DeploymentTier.Production
                ? ProductionUserNameKey
                : NonProductionUserNameKey;

            var verificationEntryKey = tier == DeploymentTier.Production
                ? ProductionVerificationEntryKey
                : NonProductionVerificationEntryKey;

            var hasSingleUserName = TryReadSingleConfigValue(userNameKey, "username", out var rawUserNameValue);
            var hasSingleVerification = TryReadSingleConfigValue(verificationEntryKey, "verification", out var rawVerificationValue);
            if (!hasSingleUserName || !hasSingleVerification)
            {
                return null;
            }

            var credential = DeploymentCredential.FromPlainText(
                rawUserNameValue,
                rawVerificationValue);

            try
            {
                if (!credential.IsComplete)
                {
                    bool userNameEmpty = string.IsNullOrWhiteSpace(rawUserNameValue);
                    bool verificationEmpty = string.IsNullOrEmpty(rawVerificationValue);
                    credential.Dispose();

                    // Named rather than silent. A caller that proceeds without a credential
                    // authenticates as whatever the host is running as, and the previous copies of
                    // this logic reported only that "a valid DOrc Username or Password" was missing
                    // without saying which entry was empty.
                    Logger.LogError(
                        "No {Tier} deployment credential could be resolved from {Source}. Username entry is {UserNameState} and verification entry is {VerificationState}.",
                        tier,
                        Description,
                        userNameEmpty ? "empty" : "set",
                        verificationEmpty ? "empty" : "set");

                    return null;
                }

                return credential;
            }
            catch
            {
                credential.Dispose();
                throw;
            }
        }

        private bool TryReadSingleConfigValue(string configEntryName, string roleLabel, out string value)
        {
            try
            {
                value = _configValues.GetConfigValue(configEntryName) ?? string.Empty;
                return true;
            }
            catch (InvalidOperationException exception)
            {
                Logger.LogWarning(
                    exception,
                    "Multiple configuration values match deployment credential {EntryRole} entry across production classifications; refusing ambiguous credential resolution.",
                    roleLabel);
                value = string.Empty;
                return false;
            }
        }
    }
}
