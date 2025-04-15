namespace Dorc.Core.Interfaces
{
    public interface IActiveDirectoryUserGroupReader
    {
        string? GetGroupSidIfUserIsMember(string userName, string groupName);
        string GetUserMail(string userName);
    }
}