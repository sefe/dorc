using System.Configuration.Provider;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Utils;
using log4net;
using Microsoft.Extensions.Caching.Memory;

namespace Dorc.Api.Services
{
    [SupportedOSPlatform("windows")]
    public class ActiveDirectoryUserGroupReader : IActiveDirectoryUserGroupReader
    {
        private readonly string _domainName;
        private readonly IMemoryCache _cache;
        private readonly IActiveDirectorySearcher _activeDirectorySearcher;
        private readonly ILog _logger;
        private readonly TimeSpan? _cacheExpiration;

        public ActiveDirectoryUserGroupReader(IConfigurationSettings config, IMemoryCache cache, IActiveDirectorySearcher activeDirectorySearcher, ILog log)
        {
            _domainName = config.GetConfigurationDomainNameIntra();
            _cacheExpiration = config.GetADUserCacheTimeSpan();
            _cache = cache;
            _activeDirectorySearcher = activeDirectorySearcher;
            _logger = log;
        }

        public string? GetGroupSidIfUserIsMember(string userName, string groupName)
        {
            var cacheKey = $"{userName}:{groupName}";
            if (_cacheExpiration.HasValue && _cache.TryGetValue(cacheKey, out string? cachedSid))
            {
                return cachedSid;
            }

            var sid = getGroupSidForUser(userName, groupName);
            if (_cacheExpiration.HasValue && sid != null)
            {
                _cache.Set(cacheKey, sid, _cacheExpiration.Value);
            }

            return sid;
        }

        public string GetUserMail(string userName)
        {
            var cacheKey = $"{userName}";
            if (_cacheExpiration.HasValue && _cache.TryGetValue(cacheKey, out string? cachedEmail))
            {
                return cachedEmail;
            }

            var directoryEntry = _activeDirectorySearcher.GetUserIdActiveDirectory(userName);

            var email = directoryEntry.Email;

            if (_cacheExpiration.HasValue && email != null)
            {
                _cache.Set(cacheKey, email, _cacheExpiration.Value);
            }

            return email;
        }

        private string? getGroupSidForUser(string userName, string groupName)
        {
            using (var profiler = new TimeProfiler(this._logger, "getGroupSidForUser"))
            using (var context = new PrincipalContext(ContextType.Domain, null, _domainName))
            {
                try
                {
                    using (var groupPrincipal = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, groupName))
                    {
                        profiler.LogTime("Group FindByIdentity");
                        if (groupPrincipal != null)
                        {
                            var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
                            profiler.LogTime("User FindByIdentity");
                            if (userPrincipal != null && groupPrincipal.GetMembers(true).Contains(userPrincipal))
                            {
                                return groupPrincipal.Sid.Value;
                            }
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
