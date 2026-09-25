using Dorc.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.Security
{
    /// <summary>
    /// Resolves the deployment credential from the secrets vault rather than from DOrc's own
    /// configuration values.
    ///
    /// This is the migration target, and choosing it is what removes the credentials from
    /// DOrc's database altogether. It is not the default: switching where the whole estate's
    /// deployment credentials come from is a deliberate act, not something a release should do
    /// on someone's behalf.
    ///
    /// The vault reader lives in this assembly rather than the API's precisely so that the
    /// Monitor can use it. Whether the Monitor hosts can reach the vault is then a deployment
    /// question with a configured answer, not an architectural dependency — which is the point
    /// of having two implementations rather than moving everything to one.
    /// </summary>
    public class VaultDeploymentCredentialSource : IDeploymentCredentialSource
    {
        private readonly IConfigurationSecretsReader _secrets;
        private readonly IConfigurationSettings _configuration;
        private readonly ILogger _logger;

        public VaultDeploymentCredentialSource(
            IConfigurationSecretsReader secrets,
            IConfigurationSettings configuration,
            ILogger<VaultDeploymentCredentialSource> logger)
        {
            _secrets = secrets;
            _configuration = configuration;
            _logger = logger;
        }

        public string Description => "the secrets vault";

        public DeploymentCredential? Resolve(DeploymentTier tier)
        {
            var userNameItem = _configuration.GetDeploymentCredentialItemId(tier, forPassword: false);
            var passwordItem = _configuration.GetDeploymentCredentialItemId(tier, forPassword: true);

            if (string.IsNullOrWhiteSpace(userNameItem) || string.IsNullOrWhiteSpace(passwordItem))
            {
                // Selected as the source but not told where to look. Refusing is the only safe
                // answer: falling back to configuration values would silently undo the choice to
                // stop keeping deployment credentials in DOrc's database.
                _logger.LogError(
                    "The {Tier} deployment credential is configured to come from {Source}, but its"
                    + " vault item identifiers are not set. Refusing to fall back to configuration"
                    + " values.",
                    tier,
                    Description);

                return null;
            }

            var credential = new DeploymentCredential(
                _secrets.GetSecret(userNameItem, $"{tier} deployment username"),
                _secrets.GetSecureSecret(passwordItem, $"{tier} deployment password"));

            if (!credential.IsComplete)
            {
                _logger.LogError(
                    "No {Tier} deployment credential could be retrieved from {Source}."
                    + " The username item returned {UserNameState} and the password item returned"
                    + " {PasswordState}.",
                    tier,
                    Description,
                    string.IsNullOrEmpty(credential.UserName) ? "nothing" : "a value",
                    credential.Password.Length == 0 ? "nothing" : "a value");

                return null;
            }

            return credential;
        }
    }
}
