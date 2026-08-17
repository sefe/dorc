using Dorc.ApiModel;

namespace Dorc.Api.Interfaces
{
    public interface IUserGroupReader
    {
        string? FindGroupIfMemberByName(string userName, string groupName);
        string GetUserMail(string userName);
        DirectoryPrincipalApiModel FindByName(string userName);
        List<string> GetIdentifiersForUser(string username);
    }
}