using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface IActiveDirectorySearcher
    {
        List<UserElementApiModel> Search(string objectName);
        UserElementApiModel GetUserData(string name);
        UserElementApiModel GetUserDataById(string id);
        List<string> GetSidsForUser(string username);
        string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName);
    }
}