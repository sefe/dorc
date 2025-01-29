using System.Collections.Concurrent;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dorc.PersistentData.Extensions
{
    [SupportedOSPlatform("windows")]
    public static class UserExtensions
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> SidCache = new ConcurrentDictionary<string, CacheEntry>();
        public static TimeSpan? CacheDuration;

        private class CacheEntry
        {
            public List<string> Sids { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public static List<string> GetSidsForUser(this IPrincipal user)
        {
            return (user.Identity?.Name).GetSidsForUser();
        }

        public static List<string> GetSidsForUser(this string username)
        {
            if (CacheDuration is not null && SidCache.TryGetValue(username, out var cacheEntry) && (DateTime.Now - cacheEntry.Timestamp) < CacheDuration)
            {
                return cacheEntry.Sids;
            }

            var result = new HashSet<string>();
            var name = username;

            DirectorySearcher ds = new DirectorySearcher();
            if (username.Contains('\\'))
                name = username.Split('\\')[1];

            ds.Filter = $"(&(objectClass=user)(sAMAccountName={name}))";
            SearchResult sr = ds.FindOne();

            DirectoryEntry user = sr.GetDirectoryEntry();
            user.RefreshCache(new string[] { "tokenGroups" });

            for (int i = 0; i < user.Properties["tokenGroups"].Count; i++)
            {
                SecurityIdentifier sid = new SecurityIdentifier((byte[])user.Properties["tokenGroups"][i], 0);
                result.Add(sid.ToString());
            }

            var f = new NTAccount(username);
            var s = (SecurityIdentifier)f.Translate(typeof(SecurityIdentifier));
            var sidString = s.ToString();

            result.Add(sidString);

            var sidList = result.ToList();
            if (CacheDuration is not null)
                SidCache[username] = new CacheEntry { Sids = sidList, Timestamp = DateTime.Now };

            return sidList;
        }

        public static string GetUsername(this IPrincipal user)
        {
            var userSplit = user.Identity.Name.Split('\\');
            var userName = userSplit[1];
            return userName;
        }
    }
}
