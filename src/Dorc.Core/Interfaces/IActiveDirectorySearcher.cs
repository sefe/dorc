using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface IActiveDirectorySearcher
    {
        List<ActiveDirectoryElementApiModel> Search(string objectName);
        ActiveDirectoryElementApiModel GetUserIdActiveDirectory(string id);
        List<string> GetSidsForUser(string username);
        string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName);
    }
}