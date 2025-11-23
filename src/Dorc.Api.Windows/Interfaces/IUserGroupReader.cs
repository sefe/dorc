using Dorc.ApiModel;

namespace Dorc.Api.Windows.Interfaces
{
    public interface IUserGroupReader
    {
        string? GetGroupSidIfUserIsMember(string userName, string groupName);
        string GetUserMail(string userName);
        UserElementApiModel GetUserData(string userName);
        List<string> GetSidsForUser(string username);
    }
}