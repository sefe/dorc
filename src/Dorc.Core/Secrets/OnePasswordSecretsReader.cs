using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Microsoft.Extensions.Logging;
using System.Security;
using Dorc.Core.Secrets.OnePassword;

namespace Dorc.Core.Secrets
{
    /// <summary>
    /// Reads secrets from 1Password.
    ///
    /// Relocated out of the API assembly, where it could not be reached by the Monitor at all,
    /// and out of a `Services` namespace, which the repository's naming standard names as a
    /// dumping ground to avoid. Credential resolution needs it in both processes, so leaving it
    /// where it was would have meant a second copy.
    /// </summary>
    public class OnePasswordSecretsReader : IConfigurationSecretsReader
    {
        private readonly ILogger _log;
        private readonly IConfigurationSettings _config;
        private readonly OnePasswordClient? _onePasswordClient;
        private readonly string? _vaultId;

        public OnePasswordSecretsReader(IConfigurationSettings config, ILogger<OnePasswordSecretsReader> logger)
        {
            _log = logger;
            _config = config;
            _vaultId = config.GetOnePasswordVaultId();
            var baseUrl = config.GetOnePasswordBaseUrl();
            var apiKey = config.GetOnePasswordApiKey();
            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    _onePasswordClient = new OnePasswordClient(baseUrl, apiKey);
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"Failed to initialize OnePasswordClient: {ex.Message}");
                }
            }
        }

        public string GetIdentityServerApiSecret()
        {
            var itemId = _config.GetOnePasswordIdentityServerApiSecretItemId();
            return GetSecretByItemId(itemId, "IdentityServer API secret");
        }

        public string GetDorcApiSecret()
        {
            var itemId = _config.GetOnePasswordItemId();
            return GetSecretByItemId(itemId, "DORC API secret");
        }

        public string GetSecret(string itemId, string humanizedName) =>
            GetSecretByItemId(itemId, humanizedName);

        public SecureString GetSecureSecret(string itemId, string humanizedName)
        {
            var value = GetSecretByItemId(itemId, humanizedName);
            return DeploymentCredential.ToSecureString(value);
        }

        private string GetSecretByItemId(string itemId, string humanizedName)
        {
            if (_onePasswordClient == null || string.IsNullOrEmpty(_vaultId) || string.IsNullOrEmpty(itemId))
            {
                return string.Empty;
            }

            try
            {
                return _onePasswordClient.GetSecretValueAsync(_vaultId, itemId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to get secret {humanizedName} from 1Password by ItemId {itemId}: {ex.Message}");
                return string.Empty;
            }
        }
    }
} 