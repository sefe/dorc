namespace Dorc.Api.Interfaces
{
    public interface IActiveDirectoryService
    {
        Task<string?> GetGroupSidIfUserIsMemberAsync(string userName, string groupName);
    }
}
