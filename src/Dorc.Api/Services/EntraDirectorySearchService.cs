using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Services
{
    // Graph-backed implementation of the search surface DirectorySearchController exposes.
    // Replaces ActiveDirectorySearchService (which depended on System.DirectoryServices).
    public class EntraDirectorySearchService : IDirectorySearchService
    {
        private readonly IActiveDirectorySearcher _searcher;
        private readonly string _intraDomainName;

        public EntraDirectorySearchService(IActiveDirectorySearcher searcher, IConfigurationSettings config)
        {
            _searcher = searcher;
            _intraDomainName = config.GetConfigurationDomainNameIntra();
        }

        public IList<UserSearchResult> FindUsers(string searchCriteria, string domainName)
        {
            return _searcher.Search(searchCriteria)
                .Where(e => !e.IsGroup)
                .Select(e => new UserSearchResult
                {
                    DisplayName = e.DisplayName,
                    FullLogonName = $@"{domainName}\{ResolveLogonName(e)}"
                })
                .ToList();
        }

        public IList<GroupSearchResult> FindGroups(string searchCriteria, string domainName)
        {
            return _searcher.Search(searchCriteria)
                .Where(e => e.IsGroup)
                .Select(e => new GroupSearchResult
                {
                    DisplayName = e.DisplayName,
                    FullLogonName = $@"{domainName}\{e.DisplayName ?? e.Username}"
                })
                .ToList();
        }

        public bool IsUserInGroup(string groupName, string account, string domainName)
        {
            var groupId = _searcher.GetGroupSidIfUserIsMemberRecursive(account, groupName, domainName);
            return !string.IsNullOrEmpty(groupId);
        }

        private static string? ResolveLogonName(UserElementApiModel e)
        {
            if (!string.IsNullOrEmpty(e.Username))
            {
                var at = e.Username.IndexOf('@');
                return at > 0 ? e.Username[..at] : e.Username;
            }
            return e.DisplayName;
        }
    }
}
