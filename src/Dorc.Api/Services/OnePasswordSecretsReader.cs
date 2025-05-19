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

        //public AzureEntraCredentials GetAzureEntraCredentials()
        //{
        //    if (_onePasswordClient == null || string.IsNullOrEmpty(_vaultId) || string.IsNullOrEmpty(_azureEntraItemId))
        //    {
        //        return new AzureEntraCredentials();
        //    }

        //    try
        //    {
        //        var item = _onePasswordClient.GetItemAsync(_vaultId, _azureEntraItemId).GetAwaiter().GetResult();
        //        if (item != null)
        //        {
        //            var credentials = new AzureEntraCredentials();

        //            // Extract credentials based on field labels
        //            foreach (var field in item.Fields)
        //            {
        //                switch (field.Label.ToLower())
        //                {
        //                    case "tenant id":
        //                        credentials.TenantId = field.Value;
        //                        break;
        //                    case "client id":
        //                        credentials.ClientId = field.Value;
        //                        break;
        //                    case "client secret":
        //                        credentials.ClientSecret = field.Value;
        //                        break;
        //                }
        //            }

        //            if (!string.IsNullOrEmpty(credentials.TenantId) &&
        //                !string.IsNullOrEmpty(credentials.ClientId) &&
        //                !string.IsNullOrEmpty(credentials.ClientSecret))
        //            {
        //                _log.Info("Successfully retrieved Azure Entra credentials from 1Password");
        //                return credentials;
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _log.Warn($"Error retrieving Azure Entra credentials from 1Password: {ex.Message}");
        //    }

        //    return new AzureEntraCredentials();
        //}

        public string GetDorcApiSecret()
        {
            var dorcApiItemId = _config.GetOnePasswordItemId();
            if (_onePasswordClient == null || string.IsNullOrEmpty(_vaultId) || string.IsNullOrEmpty(dorcApiItemId))
            {
                return string.Empty;
            }

            try
            {
                string secret = _onePasswordClient.GetSecretValueAsync(_vaultId, dorcApiItemId).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(secret))
                {
                    _log.Info("Successfully retrieved DORC API secret from 1Password");
                    return secret;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Error retrieving DORC API secret from 1Password: {ex.Message}", ex);
            }

            return string.Empty;
        }
    }
} 