using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Text.Json;
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
                catch (ArgumentException ex)
                {
                    LogClientInitializationFailure(ex);
                }
                catch (FormatException ex)
                {
                    LogClientInitializationFailure(ex);
                }
                catch (InvalidOperationException ex)
                {
                    LogClientInitializationFailure(ex);
                }
            }
        }

        internal OnePasswordSecretsReader(
            IConfigurationSettings config,
            OnePasswordClient? onePasswordClient,
            ILogger<OnePasswordSecretsReader> logger)
        {
            _log = logger;
            _config = config;
            _vaultId = config.GetOnePasswordVaultId();
            _onePasswordClient = onePasswordClient;
        }

        public string GetIdentityServerApiSecret()
        {
            var itemId = _config.GetOnePasswordIdentityServerApiSecretItemId();
            return GetSecretByItemId(itemId ?? string.Empty, "IdentityServer API entry");
        }

        public string GetDorcApiSecret()
        {
            var itemId = _config.GetOnePasswordItemId();
            return GetSecretByItemId(itemId ?? string.Empty, "DORC API entry");
        }

        public SecureString GetSecureSecret(string itemId, string entryLabel)
        {
            if (_onePasswordClient == null || string.IsNullOrWhiteSpace(_vaultId) || string.IsNullOrWhiteSpace(itemId))
            {
                return DeploymentCredential.ToSecureString(string.Empty);
            }

            try
            {
                return _onePasswordClient.GetSecureSecretAsync(_vaultId, itemId.Trim()).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (OperationCanceledException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (IOException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (JsonException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (InvalidOperationException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (ArgumentException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (FormatException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (SecurityException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }

            return DeploymentCredential.ToSecureString(string.Empty);
        }

        public string GetPrincipalIdentifierByItemId(string itemId, string entryLabel)
        {
            if (_onePasswordClient == null || string.IsNullOrWhiteSpace(_vaultId) || string.IsNullOrWhiteSpace(itemId))
            {
                return string.Empty;
            }

            try
            {
                bool allowFallbackField = !SharesVaultEntryWithTierVerification(itemId);
                return _onePasswordClient
                    .GetPrincipalIdentifierAsync(_vaultId, itemId.Trim(), allowFallbackField: allowFallbackField)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (HttpRequestException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (OperationCanceledException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (IOException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (JsonException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (InvalidOperationException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (ArgumentException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (FormatException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (SecurityException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }

            return string.Empty;
        }

        public string GetSecretByItemId(string itemId, string entryLabel)
        {
            if (_onePasswordClient == null || string.IsNullOrWhiteSpace(_vaultId) || string.IsNullOrWhiteSpace(itemId))
            {
                return string.Empty;
            }

            try
            {
                return _onePasswordClient.GetSecretValueAsync(_vaultId, itemId.Trim()).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (OperationCanceledException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (IOException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (JsonException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (InvalidOperationException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (ArgumentException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (FormatException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }
            catch (SecurityException ex)
            {
                LogRetrievalFailure(ex, entryLabel);
            }

            return string.Empty;
        }

        private void LogClientInitializationFailure(Exception ex)
        {
            _log.LogWarning(ex, "Failed to initialize OnePasswordClient.");
        }

        private void LogRetrievalFailure(Exception ex, string entryLabel)
        {
            string safeLabel = (entryLabel ?? string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
            _log.LogError(ex, "Failed to retrieve {EntryLabel} from 1Password.", safeLabel);
        }

        private bool SharesVaultEntryWithTierVerification(string itemId)
        {
            string trimmed = itemId.Trim();
            string? prodSecondaryItem = _config.GetDeploymentCredentialItemId(DeploymentTier.Production, true);
            string? nonProdSecondaryItem = _config.GetDeploymentCredentialItemId(DeploymentTier.NonProduction, true);

            return (!string.IsNullOrWhiteSpace(prodSecondaryItem)
                    && string.Equals(trimmed, prodSecondaryItem.Trim(), StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(nonProdSecondaryItem)
                    && string.Equals(trimmed, nonProdSecondaryItem.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
} 