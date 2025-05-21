using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface IActiveDirectorySearcher
    {
        List<ActiveDirectoryElementApiModel> Search(string objectName);
        ActiveDirectoryElementApiModel GetUserData(string name);
        ActiveDirectoryElementApiModel GetEntityById(string id);
        List<string> GetSidsForUser(string username);
        string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName);
    }
}