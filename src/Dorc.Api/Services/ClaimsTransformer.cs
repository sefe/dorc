﻿using System.Configuration.Provider;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Dorc.Api.Interfaces;
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
        private readonly IActiveDirectoryUserGroupReader _adUserGroupReader;
        private readonly List<AdPermittedGroup> _permittedRoleGroups;

        public ClaimsTransformer(IConfiguration config, IActiveDirectoryUserGroupReader adUserGroupReader)
        {
            _adUserGroupReader = adUserGroupReader;

            var activeDirectoryRoles = config.GetSection("AppSettings:ActiveDirectoryRoles").GetChildren()
                .ToDictionary(x => x.Key, x => x.Value);

            _permittedRoleGroups = activeDirectoryRoles
                .Select(x => new AdPermittedGroup
                {
                    Name = x.Value?.Trim(),
                    FriendlyName = x.Key.Trim()
                })
                .ToList();
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var claims = new List<Claim>();
            string authenticationType = principal.Identity?.AuthenticationType ?? string.Empty;
            string currentUserName = principal.Identity?.Name ?? string.Empty;

            if (!string.IsNullOrEmpty(currentUserName))
            {
                claims.Add(new Claim(ClaimTypes.Name, currentUserName));
            }

            if (principal.Identity is WindowsIdentity windowsIdentity && windowsIdentity.User != null)
            {
                claims.Add(new Claim(ClaimTypes.Sid, windowsIdentity.User.Value));
            }

            // Add role claims based on AD group membership
            foreach (var adPermittedGroup in _permittedRoleGroups)
            {
                if (string.IsNullOrEmpty(adPermittedGroup.Name))
                {
                    continue;
                }

                var containingGroupSid = _adUserGroupReader.GetGroupSidIfUserIsMember(currentUserName, adPermittedGroup.Name);
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
            return new ClaimsPrincipal(newClaimsIdentity);
        }
    }
}
