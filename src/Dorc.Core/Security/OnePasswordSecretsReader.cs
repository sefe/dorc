using Dorc.Core.Configuration;
using Dorc.Core.Security.OnePassword;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.Security
{
    /// <summary>
    /// Manages secrets using 1Password
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
