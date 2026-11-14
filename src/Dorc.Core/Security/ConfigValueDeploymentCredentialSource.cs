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
        internal const string ProductionVerificationEntryKey = "DORC_ProdDeployPassword";
        internal const string NonProductionUserNameKey = "DORC_NonProdDeployUsername";
        internal const string NonProductionVerificationEntryKey = "DORC_NonProdDeployPassword";

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

            var verificationEntryKey = tier == DeploymentTier.Production
                ? ProductionVerificationEntryKey
                : NonProductionVerificationEntryKey;

            var rawUserNameValue = _configValues.GetConfigValue(userNameKey) ?? string.Empty;
            var rawVerificationValue = _configValues.GetConfigValue(verificationEntryKey) ?? string.Empty;

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
                    _logger.LogError(
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
    }
}
