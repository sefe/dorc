using System;
using System.Globalization;
using System.Linq;

namespace Org.OpenAPITools.Client.Auth
{
    /// <summary>
    /// ConnectionSettings object contains values for Connecting to Azure Active Directory (AAD)
    /// </summary>
    public class AadConnectionSettings
    {
        #region Properties
        public string ClientSecret { get; private set; }
        public string Authority { get; private set; }
        public string ClientId { get; private set; }
        public string TenantId { get; private set; }
        public string AadInstance { get; private set; }
        public string AzureDevOpsOrganizationalUrl { get; private set; }
        public string[] Scopes { get; private set; }
        #endregion

        /// <summary>
        /// Constructor for AAD ConnectionSettings values for connecting to AAD.
        /// This also contains validation checks to ensure the values are valid for use for Token generation
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="aadInstance"></param>
        /// <param name="azureDevOpsOrganizationalUrl"></param>
        /// <param name="scopes"></param>
        /// <param name="clientSecret"></param>
        /// <param name="tenantId"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public AadConnectionSettings(string? clientId, string? aadInstance,
            string? azureDevOpsOrganizationalUrl, string?[]? scopes = null, string? clientSecret=null,
            string? tenantId= "213c2807-792f-48e1-924a-eac984ef3354")
        {
            #region Validation

            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentNullException(nameof(clientId));
            }
            if (string.IsNullOrEmpty(aadInstance))
            {
                throw new ArgumentNullException(nameof(aadInstance));
            }
            if (string.IsNullOrEmpty(azureDevOpsOrganizationalUrl))
            {
                throw new ArgumentNullException(nameof(azureDevOpsOrganizationalUrl));
            }
            if (string.IsNullOrEmpty(clientSecret))
            {
                throw new ArgumentNullException(nameof(clientSecret));
            }

            #endregion

            this.Scopes = scopes?.Where(s => s != null).Cast<string>().ToArray() ?? new[] { ".default" };
            this.ClientId = clientId!;
            this.AadInstance = aadInstance!;
            this.AzureDevOpsOrganizationalUrl = azureDevOpsOrganizationalUrl!;
            this.ClientSecret = clientSecret!;
            this.TenantId = tenantId ?? "213c2807-792f-48e1-924a-eac984ef3354";
            this.Authority = string.Format(CultureInfo.InvariantCulture, this.AadInstance, this.TenantId);
        }

    }
}
