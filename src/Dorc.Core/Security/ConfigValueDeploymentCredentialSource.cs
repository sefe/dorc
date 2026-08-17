using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

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
        internal const string ProductionPasswordKey = "DORC_ProdDeployPassword";
        internal const string NonProductionUserNameKey = "DORC_NonProdDeployUsername";
        internal const string NonProductionPasswordKey = "DORC_NonProdDeployPassword";

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
            var credential = new DeploymentCredential(
                _configValues.GetConfigValue($"DORC_Deploy_{identityReference}_Username") ?? string.Empty,
                _configValues.GetConfigValue($"DORC_Deploy_{identityReference}_Password") ?? string.Empty);

            return credential.IsComplete ? credential : null;
        }

        protected override DeploymentCredential? ResolveTierDefault(DeploymentTier tier)
        {
            var userNameKey = tier == DeploymentTier.Production
                ? ProductionUserNameKey
                : NonProductionUserNameKey;

            var passwordKey = tier == DeploymentTier.Production
                ? ProductionPasswordKey
                : NonProductionPasswordKey;

            var credential = new DeploymentCredential(
                _configValues.GetConfigValue(userNameKey) ?? string.Empty,
                _configValues.GetConfigValue(passwordKey) ?? string.Empty);

            if (!credential.IsComplete)
            {
                // Named rather than silent. A caller that proceeds without a credential
                // authenticates as whatever the host is running as, and the previous copies of
                // this logic reported only that "a valid DOrc Username or Password" was missing
                // without saying which key was empty.
                Logger.LogError(
                    "No {Tier} deployment credential could be resolved from {Source}."
                    + " '{UserNameKey}' is {UserNameState} and '{PasswordKey}' is {PasswordState}.",
                    tier,
                    Description,
                    userNameKey,
                    string.IsNullOrEmpty(credential.UserName) ? "empty" : "set",
                    passwordKey,
                    string.IsNullOrEmpty(credential.Password) ? "empty" : "set");

                return null;
            }

            return credential;
        }
    }
}
