using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using log4net;

namespace Dorc.Core
{
    public class CompositeActiveDirectorySearcher : IActiveDirectorySearcher
    {
        private readonly IEnumerable<IActiveDirectorySearcher> _searchers;
        private readonly ILog _log;

        public CompositeActiveDirectorySearcher(IEnumerable<IActiveDirectorySearcher> searchers, ILog log)
        {
            _searchers = searchers ?? throw new ArgumentNullException(nameof(searchers));
            _log = log;
        }

        public List<UserElementApiModel> Search(string objectName)
        {
            var results = new List<UserElementApiModel>();
            var seenPids = new HashSet<string>();

            foreach (var searcher in _searchers)
            {
                try
                {
                    var searcherResults = searcher.Search(objectName);
                    foreach (var result in searcherResults)
                    {
                        // Only add if we haven't seen this PID before
                        if (!string.IsNullOrEmpty(result.Pid) && seenPids.Add(result.Pid))
                        {
                            results.Add(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Error searching with {searcher.GetType().Name}: {ex.Message}", ex);
                    // Continue with other searchers even if one fails
                }
            }

            return results;
        }

        public UserElementApiModel GetUserData(string name)
        {
            foreach (var searcher in _searchers)
            {
                try
                {
                    return searcher.GetUserData(name);
                }
                catch (Exception ex)
                {
                    _log.Debug($"Error getting user data with {searcher.GetType().Name}: {ex.Message}", ex);
                    // Continue with other searchers
                }
            }

            throw new ArgumentException($"User with name '{name}' not found in any source");
        }

        public UserElementApiModel GetUserDataById(string id)
        {
            foreach (var searcher in _searchers)
            {
                try
                {
                    return searcher.GetUserDataById(id);
                }
                catch (Exception ex)
                {
                    _log.Debug($"Error getting user data by ID with {searcher.GetType().Name}: {ex.Message}", ex);
                    // Continue with other searchers
                }
            }

            throw new ArgumentException($"User with ID '{id}' not found in any source");
        }

        public List<string> GetSidsForUser(string username)
        {
            var allSids = new HashSet<string>();

            foreach (var searcher in _searchers)
            {
                try
                {
                    var sids = searcher.GetSidsForUser(username);
                    foreach (var sid in sids)
                    {
                        allSids.Add(sid);
                    }
                }
                catch (Exception ex)
                {
                    _log.Debug($"Error getting SIDs with {searcher.GetType().Name}: {ex.Message}", ex);
                    // Continue with other searchers
                }
            }

            return allSids.ToList();
        }

        public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
        {
            foreach (var searcher in _searchers)
            {
                try
                {
                    var groupSid = searcher.GetGroupSidIfUserIsMemberRecursive(userName, groupName, domainName);
                    if (groupSid != null)
                    {
                        return groupSid;
                    }
                }
                catch (Exception ex)
                {
                    _log.Debug($"Error checking group membership with {searcher.GetType().Name}: {ex.Message}", ex);
                    // Continue with other searchers
                }
            }

            return null;
        }
    }
}
