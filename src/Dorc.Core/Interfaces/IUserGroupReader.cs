using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface IUserGroupReader
    {
        string? GetGroupSidIfUserIsMember(string userName, string groupName);
        string GetUserMail(string userName);
        ActiveDirectoryElementApiModel GetUserData(string userName);
        List<string> GetSidsForUser(string username);
    }
}