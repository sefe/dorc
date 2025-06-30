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
