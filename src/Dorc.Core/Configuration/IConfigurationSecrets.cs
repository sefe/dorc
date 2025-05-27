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
    }
} 