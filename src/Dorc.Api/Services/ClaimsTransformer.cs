using System.Configuration.Provider;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication;

namespace Dorc.Api.Services
{
    public class AdPermittedGroup
    {
        public string? Name { set; get; }
        public string? FriendlyName { set; get; }
    }

    [SupportedOSPlatform("windows")]
    public class ClaimsTransformer : IClaimsTransformation
    {
        private readonly List<AdPermittedGroup> permittedRoleGroups = new();
        private readonly string? _fullQualifiedDomainName;

        public ClaimsTransformer(IConfigurationRoot configRoot)
        {

            _fullQualifiedDomainName = configRoot.GetSection("AppSettings")[
                "DomainNameIntra"];

            var activeDirectoryRoles =
                configRoot.GetSection("AppSettings")
                    .GetSection("ActiveDirectoryRoles").GetChildren()
                    .ToDictionary(x => x.Key, x => x.Value);

            foreach (var activeDirectoryRole in activeDirectoryRoles)
            {
                permittedRoleGroups.Add(new AdPermittedGroup
                {
                    Name = activeDirectoryRole.Value?.Trim(),
                    FriendlyName = activeDirectoryRole.Key.Trim()
                });
            }
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var claims = new List<Claim>();
            string authenticationType = string.Empty;
            string currentUserName = string.Empty;
            if (principal.Identity != null)
            {
                var claimsIdentity = (ClaimsIdentity)principal.Identity;
                if (!string.IsNullOrEmpty(claimsIdentity.AuthenticationType))
                {
                    authenticationType = claimsIdentity.AuthenticationType;
                }
                if (!string.IsNullOrEmpty(claimsIdentity.Name))
                {
                    currentUserName = claimsIdentity.Name;
                    claims.Add(new Claim(ClaimTypes.Name, currentUserName));
                }
            }

            if (principal.Identity is WindowsIdentity)
            {
                var windowsIdentity = (WindowsIdentity)principal.Identity;
                if (windowsIdentity.User != null)
                {
                    var currentUserSid = windowsIdentity.User.Value;
                    claims.Add(new Claim(ClaimTypes.Sid, currentUserSid));
                }
            }

            // Find the user in the DB
            // Add as many group claims as they have roles in the DB
            foreach (var adPermittedGroup in permittedRoleGroups)
            {
                if (string.IsNullOrEmpty(adPermittedGroup.Name))
                {
                    continue;
                }

                string? containingGroupSid = GetFirstAdGroupSidIfUserInIt(currentUserName, adPermittedGroup.Name);
                if (!string.IsNullOrEmpty(containingGroupSid))
                {
                    if (!string.IsNullOrEmpty(adPermittedGroup.FriendlyName))
                    {
                        claims.Add(new Claim(ClaimTypes.Role, adPermittedGroup.FriendlyName));
                    }

                    claims.Add(new Claim(ClaimTypes.Sid, containingGroupSid));
                }
            }

            // Build and return the new principal
            var newClaimsIdentity = new ClaimsIdentity(claims, authenticationType);

            ClaimsPrincipal claimsPrincipal = new ClaimsPrincipal(newClaimsIdentity);

            return claimsPrincipal;
        }

        private string? GetFirstAdGroupSidIfUserInIt(string currentUserNameWithDomain, string adPermittedGroupName)
        {
            var currentUserName = currentUserNameWithDomain.Split("\\")[1];

            using (var context = new PrincipalContext(ContextType.Domain, null, _fullQualifiedDomainName))
            {
                try
                {
                    GroupPrincipal groupPrincipal = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, adPermittedGroupName);

                    PrincipalSearchResult<Principal> principals = groupPrincipal.GetMembers(true);
                    foreach (UserPrincipal user in principals)
                    {
                        if (currentUserName.Equals(user.SamAccountName))
                        {
                            return groupPrincipal.Sid.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new ProviderException("Unable to query Active Directory.", ex);
                }
            }

            return null;
        }
    }
}
