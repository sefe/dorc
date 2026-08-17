using System.Threading.Tasks;

namespace Dorc.Core.Configuration
{
    /// <summary>
    /// Interface for retrieving sensitive configuration values
    /// </summary>
    public interface IConfigurationSecretsReader
    {
        /// <summary>
        /// Gets the API secret for IdentityServer to use in DORC for introspection token
        /// </summary>
        string GetDorcApiSecret();
        
        /// <summary>
        /// Gets the API secret for IdentityServer client credentials
        /// </summary>
        string GetIdentityServerApiSecret();

        /// <summary>
        /// Gets an arbitrary secret by its vault item identifier, or the empty string when it
        /// cannot be retrieved.
        /// </summary>
        /// <param name="itemId">The vault item identifier, taken from configuration.</param>
        /// <param name="humanizedName">
        /// What the secret is, for log messages. Never the value.
        /// </param>
        /// <remarks>
        /// Added so that credential resolution can be backed by the vault without a second
        /// client. Returning empty rather than throwing matches the existing accessors: a
        /// caller has to decide what an absent secret means, and for a deployment credential
        /// the answer is to refuse rather than to authenticate as the host account.
        /// </remarks>
        string GetSecret(string itemId, string humanizedName);
    }
} 