using Dorc.Core.Configuration;
using log4net;
using OnePassword.Connect.Client;

namespace Dorc.Api.Services
{
    /// <summary>
    /// Manages secrets using 1Password
    /// </summary>
    public class OnePasswordSecretsReader : IConfigurationSecretsReader
    {
        private readonly ILog _log;
        private readonly IConfigurationSettings _config;
        private readonly OnePasswordClient? _onePasswordClient;
        private readonly string? _vaultId;

        public OnePasswordSecretsReader(IConfigurationSettings config)
        {
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType ?? typeof(DefaultExceptionHandler));
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
                    _log.Warn($"Failed to initialize OnePasswordClient: {ex.Message}");
                }
            }
        }

        public string GetIdentityServerApiSecret()
        {
            var itemId = _config.GetOnePasswordIdentityServerApiSecretId();
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
                _log.Error($"Failed to get secret {humanizedName} from 1Password by ItemId {itemId}: {ex.Message}", ex);
                return string.Empty;
            }
        }
    }
} 