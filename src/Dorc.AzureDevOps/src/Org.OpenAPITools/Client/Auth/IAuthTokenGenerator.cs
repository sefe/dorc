using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Org.OpenAPITools.Client.Auth
{
    /// <summary>
    /// For any future DI needed and importantly used by the AuthTokenGeneratorFactory
    /// </summary>
    public interface IAuthTokenGenerator
    {
        /// <summary>
        /// Gets an Open API authentication token via AAD
        /// </summary>
        /// <returns>The token</returns>
        string GetToken();
    }
}