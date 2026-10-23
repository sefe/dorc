using System;
using System.Globalization;

namespace Dorc.AzureDevOps.Client.Auth
{
    /// <summary>
    /// ConnectionSettings object contains values for Connecting to Azure Active Directory (AAD)
    /// </summary>
    public class AadConnectionSettings
    {
        #region Properties
        public string ClientSecret { get; private set; }
        public string ClientId { get; private set; }
        public string TenantId { get; private set; }
        public string[] Scopes { get; private set; }
        #endregion

        /// <summary>
        /// Constructor for AAD ConnectionSettings values for connecting to AAD.
        /// This also contains validation checks to ensure the values are valid for use for Token generation
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="scopes"></param>
        /// <param name="clientSecret"></param>
        /// <param name="tenantId">Entra tenant ID; configured at install time (AadTenant), never defaulted.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public AadConnectionSettings(string clientId,
            string[] scopes, string clientSecret, string tenantId)
        {
            #region Validation

            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentNullException(nameof(clientId));
            }
            if (string.IsNullOrEmpty(clientSecret))
            {
                throw new ArgumentNullException(nameof(clientSecret));
            }
            if (string.IsNullOrEmpty(tenantId))
            {
                throw new ArgumentNullException(nameof(tenantId));
            }

            #endregion

            this.Scopes = scopes ?? new[] { ".default" };
            this.ClientId = clientId;
            this.ClientSecret = clientSecret;
            this.TenantId = tenantId;
        }

    }
}
