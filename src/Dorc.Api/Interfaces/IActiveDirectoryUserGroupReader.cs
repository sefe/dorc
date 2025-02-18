namespace Dorc.Api.Interfaces
{
    public interface IActiveDirectoryUserGroupReader
    {
        string? GetGroupSidIfUserIsMember(string userName, string groupName);
    }
}
