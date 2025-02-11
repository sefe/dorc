namespace Dorc.Api.Interfaces
{
    public interface IActiveDirectoryUserGroupReader
    {
        Task<string?> GetGroupSidIfUserIsMemberAsync(string userName, string groupName);
    }
}
