using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.PersistentData
{
    public interface IClaimsPrincipalReader
    { 
        string GetUserName(IPrincipal user);
        string GetUserFullDomainName(IPrincipal user);
        string GetUserEmail(ClaimsPrincipal user, object externalReader);
    }
}