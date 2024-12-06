using Microsoft.Identity.Client;
using System.Threading;
using Azure.Core;
using Azure.Identity;

namespace Org.OpenAPITools.Client.Auth
{
    /// <summary>
    /// AppFlow approach for Auth
    /// </summary>
    public class AppFlowAuthTokenGenerator : BaseAuthTokenGenerator
    {
        private AadConnectionSettings aadConnectionSettings;

        public static readonly string[] DefaultScopes = new string[1]
        {
            "499b84ac-1321-427f-aa17-267ca6975798/.default"
        };

        public AppFlowAuthTokenGenerator(AadConnectionSettings aadConnectionSettings)
        {
            this.aadConnectionSettings = aadConnectionSettings;
        }

        /// <summary>
        /// Returns an Azure AD access token (client credentials). It uses an in-memory cache and it also regenerates the access token if it is expired. 
        /// </summary>
        /// <returns>Valid Azure AD access token</returns>
        public override string GetToken()
        {
            var credential = new ClientSecretCredential(aadConnectionSettings.TenantId, aadConnectionSettings.ClientId, aadConnectionSettings.ClientSecret);

            var tokenRequestContext = new TokenRequestContext(DefaultScopes);
            var token = credential.GetTokenAsync(tokenRequestContext, CancellationToken.None).Result;

            return token.Token;
        }

        private static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }
    }
}