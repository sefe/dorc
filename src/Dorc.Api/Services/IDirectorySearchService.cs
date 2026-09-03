using Dorc.ApiModel;

namespace Dorc.Api.Services
{
    public interface IDirectorySearchService
    {
        IList<UserSearchResult> FindUsers(string searchCriteria, string domainName);
        IList<GroupSearchResult> FindGroups(string searchCriteria, string domainName);
        bool IsUserInGroup(string groupName, string account, string domainName);
    }
}
