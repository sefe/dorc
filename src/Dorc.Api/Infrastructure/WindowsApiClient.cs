using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using System.Text.Json;

namespace Dorc.Api.Infrastructure
{
    /// <summary>
    /// HTTP client implementation for calling Windows-specific API endpoints
    /// Routes requests to the Windows API (Dorc.Api.Windows) which runs on Windows OS
    /// </summary>
    public class WindowsApiClient : IWindowsApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WindowsApiClient> _logger;
        private readonly string _windowsApiBaseUrl;

        public WindowsApiClient(
            HttpClient httpClient,
            IConfigurationSettings configurationSettings,
            ILogger<WindowsApiClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _windowsApiBaseUrl = configurationSettings.GetWindowsApiUrl();
        }

        public async Task<IEnumerable<UserSearchResult>> SearchUsersAsync(string searchCriteria, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_windowsApiBaseUrl}/DirectorySearch/SearchUsers?searchCriteria={Uri.EscapeDataString(searchCriteria)}",
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<IEnumerable<UserSearchResult>>(content) ?? Enumerable.Empty<UserSearchResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API SearchUsers");
                throw;
            }
        }

        public async Task<IEnumerable<GroupSearchResult>> SearchGroupsAsync(string searchCriteria, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_windowsApiBaseUrl}/DirectorySearch/SearchGroups?searchCriteria={Uri.EscapeDataString(searchCriteria)}",
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<IEnumerable<GroupSearchResult>>(content) ?? Enumerable.Empty<GroupSearchResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API SearchGroups");
                throw;
            }
        }

        public async Task<bool> UserExistsAsync(string userLanId, string accountType, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_windowsApiBaseUrl}/Account/userExists?userLanId={Uri.EscapeDataString(userLanId)}&accountType={Uri.EscapeDataString(accountType)}",
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<bool>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API UserExists");
                throw;
            }
        }

        public async Task<bool> GroupExistsAsync(string groupLanId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_windowsApiBaseUrl}/Account/groupExists?groupLanId={Uri.EscapeDataString(groupLanId)}",
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<bool>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API GroupExists");
                throw;
            }
        }

        public async Task ResetAppPasswordAsync(string serverName, string appUserName, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{_windowsApiBaseUrl}/ResetAppPassword?serverName={Uri.EscapeDataString(serverName)}&appUserName={Uri.EscapeDataString(appUserName)}",
                    null,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API ResetAppPassword");
                throw;
            }
        }

        public async Task<IEnumerable<BundledRequestsApiModel>> GetBundledRequestsAsync(List<string> projectNames, CancellationToken cancellationToken = default)
        {
            try
            {
                var queryString = string.Join("&", projectNames.Select(p => $"projectNames={Uri.EscapeDataString(p)}"));
                var response = await _httpClient.GetAsync(
                    $"{_windowsApiBaseUrl}/BundledRequests?{queryString}",
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<IEnumerable<BundledRequestsApiModel>>(content) ?? Enumerable.Empty<BundledRequestsApiModel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API GetBundledRequests");
                throw;
            }
        }

        public async Task<Dictionary<string, object>> MakeLikeProdAsync(int environmentId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{_windowsApiBaseUrl}/MakeLikeProd?environmentId={environmentId}",
                    null,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(content) ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API MakeLikeProd");
                throw;
            }
        }

        public async Task UpdateAccessControlAsync(string type, int itemId, string lanId, bool isDelete, CancellationToken cancellationToken = default)
        {
            try
            {
                var method = isDelete ? HttpMethod.Delete : HttpMethod.Post;
                var request = new HttpRequestMessage(method, $"{_windowsApiBaseUrl}/AccessControl?type={Uri.EscapeDataString(type)}&itemId={itemId}&lanId={Uri.EscapeDataString(lanId)}");
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Windows API UpdateAccessControl");
                throw;
            }
        }
    }
}
