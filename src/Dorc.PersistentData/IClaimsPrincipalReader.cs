using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.PersistentData
{
    public interface IClaimsPrincipalReader
    { 
        string GetUserName(IPrincipal user);
        string GetUserId(ClaimsPrincipal user);
        string GetUserLogin(IPrincipal user);
        string GetUserFullDomainName(IPrincipal user);
        string GetUserSafeIdentifier(IPrincipal user);
        string GetUserEmail(ClaimsPrincipal user);
        List<string> GetSidsForUser(IPrincipal user);

        /// <summary>
        /// Whether the caller is a machine rather than a person.
        ///
        /// The determination already existed, as a private test of an OAuth "m2m" claim used to
        /// decide what to call the caller in logs. It is surfaced here because the authorization
        /// layer needs it: the ReadSecrets privilege exists for service accounts belonging to
        /// live running systems, and could not be restricted to them while the only code that
        /// could tell the difference was one layer away and private.
        /// </summary>
        bool IsServicePrincipal(IPrincipal user);
    }
}