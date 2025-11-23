using Dorc.Api.Windows.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Dorc.Api.Windows.Windows.Services
{
    public class CachedUserGroupReader : IUserGroupReader
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

        public CachedUserGroupReader(IConfigurationSettings config, IMemoryCache cache, IActiveDirectorySearcher activeDirectorySearcher)
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
            var data = this.GetUserData(userName);

            return data.Email;
        }

        public UserElementApiModel GetUserData(string userName)
        {
            var cacheKey = $"{userName}";
            if (_cacheExpiration.HasValue && _cache.TryGetValue(cacheKey, out UserElementApiModel? cachedData))
            {
                return cachedData;
            }

            var data = _activeDirectorySearcher.GetUserData(userName);

            if (_cacheExpiration.HasValue && data != null)
            {
                _cache.Set(cacheKey, data, _cacheExpiration.Value);
            }

            return data;
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
