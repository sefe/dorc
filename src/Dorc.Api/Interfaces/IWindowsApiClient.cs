using Dorc.ApiModel;

namespace Dorc.Api.Interfaces
{
    /// <summary>
    /// HTTP client for calling Windows-specific API endpoints
    /// The Windows API runs on a separate process on Windows OS to handle platform-specific operations
    /// </summary>
    public interface IWindowsApiClient
    {
        Task<IEnumerable<UserSearchResult>> SearchUsersAsync(string searchCriteria, CancellationToken cancellationToken = default);
        Task<IEnumerable<GroupSearchResult>> SearchGroupsAsync(string searchCriteria, CancellationToken cancellationToken = default);
        Task<bool> UserExistsAsync(string userLanId, string accountType, CancellationToken cancellationToken = default);
        Task<bool> GroupExistsAsync(string groupLanId, CancellationToken cancellationToken = default);
        Task ResetAppPasswordAsync(string serverName, string appUserName, CancellationToken cancellationToken = default);
        Task<IEnumerable<BundledRequestsApiModel>> GetBundledRequestsAsync(List<string> projectNames, CancellationToken cancellationToken = default);
        Task<Dictionary<string, object>> MakeLikeProdAsync(int environmentId, CancellationToken cancellationToken = default);
        Task UpdateAccessControlAsync(string type, int itemId, string lanId, bool isDelete, CancellationToken cancellationToken = default);
    }
}
