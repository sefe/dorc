using System.Security;
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
        /// <param name="entryLabel">
        /// What the entry is, for log messages. Never the value.
        /// </param>
        /// <remarks>
        /// Added so that credential resolution can be backed by the vault without a second
        /// client. Returning empty rather than throwing matches the existing accessors: a
        /// caller has to decide what an absent secret means, and for a deployment credential
        /// the answer is to refuse rather than to authenticate as the host account.
        /// </remarks>
        string GetSecretByItemId(string itemId, string entryLabel);

        /// <summary>
        /// Gets a non-secret principal identifier (username) by its vault item identifier, or the
        /// empty string when it cannot be retrieved. Never reads a password field into the
        /// returned principal identifier.
        /// </summary>
        /// <param name="itemId">The vault item identifier, taken from configuration.</param>
        /// <param name="entryLabel">
        /// What the principal identifier is, for log messages. Never the value.
        /// </param>
        string GetPrincipalIdentifierByItemId(string itemId, string entryLabel);

        /// <summary>
        /// Gets a secret intended for password authentication as a read-only
        /// <see cref="SecureString"/> that the caller owns and must dispose.
        /// </summary>
        /// <remarks>
        /// Vault-backed implementations decode transport UTF-8 JSON bytes directly into clearable
        /// pooled buffers and copy the target field characters into the returned
        /// <see cref="SecureString"/> without materializing the secret field as a managed
        /// <see cref="string"/>. Callers must dispose the returned <see cref="SecureString"/>
        /// when finished, and any downstream API that explicitly requires a managed string marks
        /// the boundary where non-string handling stops.
        /// </remarks>
        SecureString GetSecureSecret(string itemId, string entryLabel);
    }
} 