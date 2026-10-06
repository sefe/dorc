using Dorc.ApiModel;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Services
{
    // Graph-backed implementation of the search surface DirectorySearchController exposes.
    // Replaces ActiveDirectorySearchService (which depended on System.DirectoryServices).
    public class EntraDirectorySearchService : IDirectorySearchService
    {
        private readonly IActiveDirectorySearcher _searcher;

        public EntraDirectorySearchService(IActiveDirectorySearcher searcher)
        {
            _searcher = searcher;
        }

        public IList<UserSearchResult> FindUsers(string searchCriteria, string domainName)
        {
            return _searcher.Search(searchCriteria)
                .Where(e => !e.IsGroup && !IsServicePrincipal(e))
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
                    FullLogonName = $@"{domainName}\{e.SamAccountName ?? e.DisplayName ?? e.Username}"
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
            // Prefer the synced sAMAccountName: ActiveDirectorySearchService built
            // DOMAIN\sAMAccountName, and the UPN local part is frequently different
            // (alice.smith@contoso.com vs asmith), which would produce a logon name that
            // resolves to nobody.
            if (!string.IsNullOrEmpty(e.SamAccountName))
            {
                return e.SamAccountName;
            }

            if (!string.IsNullOrEmpty(e.Username))
            {
                var at = e.Username.IndexOf('@');
                return at > 0 ? e.Username[..at] : e.Username;
            }
            return e.DisplayName;
        }

        // The underlying searcher also surfaces service principals (for the ACL picker,
        // where machine clients are legitimate grantees). They have no meaningful
        // DOMAIN\logonName, so keep them out of this controller's people picker. A GUID
        // Username is the service-principal signature: users carry a UPN there.
        private static bool IsServicePrincipal(UserElementApiModel e)
        {
            return Guid.TryParse(e.Username, out _);
        }
    }
}
