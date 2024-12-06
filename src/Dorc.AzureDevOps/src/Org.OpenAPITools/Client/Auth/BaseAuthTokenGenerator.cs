using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Org.OpenAPITools.Client.Auth
{
    /// <summary>
    /// Base class for 
    /// </summary>
    public abstract class BaseAuthTokenGenerator : IAuthTokenGenerator
    {
        /// <inheritdoc/>
        public virtual string GetToken()
        {
            throw new NotImplementedException();
        }
    }
}