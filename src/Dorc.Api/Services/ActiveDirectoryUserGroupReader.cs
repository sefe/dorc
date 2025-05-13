using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace Dorc.Api.Services
{
    [SupportedOSPlatform("windows")]
    public class ActiveDirectoryUserGroupReader : IUserGroupReader
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> SidCache = new ConcurrentDictionary<string, CacheEntry>();

        private class CacheEntry
        {
            public List<string> Sids { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private readonly string _domainName;
        private readonly IMemoryCache _cache;
        private readonly IActiveDirectorySearcher _activeDirectorySearcher;
        private readonly TimeSpan? _cacheExpiration;

        public ActiveDirectoryUserGroupReader(IConfigurationSettings config, IMemoryCache cache, IActiveDirectorySearcher activeDirectorySearcher)
        {
            _domainName = config.GetConfigurationDomainNameIntra();
            _cacheExpiration = config.GetADUserCacheTimeSpan();
            _cache = cache;
            _activeDirectorySearcher = activeDirectorySearcher;
        }

        public string? GetGroupSidIfUserIsMember(string userName, string groupName)
        {
            var cacheKey = $"{userName}:{groupName}";
            if (_cacheExpiration.HasValue && _cache.TryGetValue(cacheKey, out string? cachedSid))
            {
                return cachedSid;
            }

            var sid = _activeDirectorySearcher.GetGroupSidIfUserIsMemberRecursive(userName, groupName, _domainName);
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

        public List<string> GetSidsForUser(string username)
        {
            if (_cacheExpiration.HasValue && SidCache.TryGetValue(username, out var cacheEntry) && (DateTime.Now - cacheEntry.Timestamp) < _cacheExpiration)
            {
                return cacheEntry.Sids;
            }

            var sidList = _activeDirectorySearcher.GetSidsForUser(username);
            if (_cacheExpiration.HasValue)
                SidCache[username] = new CacheEntry { Sids = sidList, Timestamp = DateTime.Now };

            return sidList;
        }        
    }
}
