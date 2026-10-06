using Org.OpenAPITools.Client;

namespace Dorc.AzureDevOps.Client
{
    /// <summary>
    /// Builds the <see cref="ApiClient"/> the generated Azure DevOps API
    /// classes are constructed with, so every request deserializes through
    /// <see cref="AzureDevOpsListResponseConverter"/>. Pass the same instance
    /// as both the synchronous and asynchronous client:
    /// <c>new BuildsApi(apiClient, apiClient, configuration)</c>.
    /// </summary>
    public static class AzureDevOpsApiClientFactory
    {
        public static ApiClient Create(string basePath)
        {
            var apiClient = new ApiClient(basePath);
            apiClient.SerializerSettings.Converters.Add(new AzureDevOpsListResponseConverter());
            return apiClient;
        }
    }
}
