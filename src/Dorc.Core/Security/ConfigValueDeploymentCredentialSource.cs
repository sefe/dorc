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
    public class ConfigValueDeploymentCredentialSource : IDeploymentCredentialSource
    {
        internal const string ProductionUserNameKey = "DORC_ProdDeployUsername";
        internal const string ProductionPasswordKey = "DORC_ProdDeployPassword";
        internal const string NonProductionUserNameKey = "DORC_NonProdDeployUsername";
        internal const string NonProductionPasswordKey = "DORC_NonProdDeployPassword";

        private readonly IConfigValuesPersistentSource _configValues;
        private readonly ILogger _logger;

        public ConfigValueDeploymentCredentialSource(
            IConfigValuesPersistentSource configValues,
            ILogger<ConfigValueDeploymentCredentialSource> logger)
        {
            _configValues = configValues;
            _logger = logger;
        }

        public string Description => "DOrc configuration values";

        public DeploymentCredential? Resolve(DeploymentTier tier)
        {
            var userNameKey = tier == DeploymentTier.Production
                ? ProductionUserNameKey
                : NonProductionUserNameKey;

            var passwordKey = tier == DeploymentTier.Production
                ? ProductionPasswordKey
                : NonProductionPasswordKey;

            var password = new SecureString();
            foreach (var character in _configValues.GetConfigValue(passwordKey) ?? string.Empty)
            {
                password.AppendChar(character);
            }

            password.MakeReadOnly();
            var credential = new DeploymentCredential(
                _configValues.GetConfigValue(userNameKey) ?? string.Empty,
                password);

            if (!credential.IsComplete)
            {
                // Named rather than silent. A caller that proceeds without a credential
                // authenticates as whatever the host is running as, and the previous copies of
                // this logic reported only that "a valid DOrc Username or Password" was missing
                // without saying which key was empty.
                _logger.LogError(
                    "No {Tier} deployment credential could be resolved from {Source}."
                    + " '{UserNameKey}' is {UserNameState} and '{PasswordKey}' is {PasswordState}.",
                    tier,
                    Description,
                    userNameKey,
                    string.IsNullOrEmpty(credential.UserName) ? "empty" : "set",
                    passwordKey,
                    credential.Password.Length == 0 ? "empty" : "set");

                return null;
            }

            return credential;
        }
    }
}
