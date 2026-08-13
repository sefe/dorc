using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Core.Security
{
    /// <summary>
    /// Stub implementation of IClaimsPrincipalReader, use it for all the tools which does not require real user\client
    /// If you want to use some client app, just pass clientId and clientName to constructor
    /// </summary>
    public class DirectToolClaimsPrincipalReader: IClaimsPrincipalReader
    {
        private readonly string _clientId;
        private readonly string _clientName;

        public DirectToolClaimsPrincipalReader()
            :this("direct_toolClientId", "Direct(static) tool client")
        {            
        }

        public DirectToolClaimsPrincipalReader(string clientId, string clientName) 
        {
            _clientId = clientId;
            _clientName = clientName;
        }

        /// <summary>
        /// This reader stands in for a tool running as itself - the Monitor among them - so the
        /// caller is a machine by construction. It is not a bypass: the ReadSecrets privilege
        /// still has to be granted explicitly on the environment.
        /// </summary>
        public bool IsServicePrincipal(IPrincipal user)
        {
            return true;
        }

        public string GetUserName(IPrincipal user)
        {
            return _clientName;
        }

        public string GetUserId(ClaimsPrincipal user)
        {
            return _clientId;
        }

        public string GetUserLogin(IPrincipal user)
        {
            return _clientId;
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            return _clientId;
        }

        public string GetUserSafeIdentifier(IPrincipal user)
        {
            return _clientId;
        }

        public string GetUserEmail(ClaimsPrincipal user)
        {
            return string.Empty;
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            return new List<string> { _clientId };
        }
    }
}
