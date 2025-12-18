using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RestSharp;

namespace Dorc.Core
{
    public class ContentResponse
    {
        public string Message { get; set; } = default!;
    }

    public interface IApiCaller
    {
        ApiResult<T> Call<T>(Endpoints endpoint, Method method, Dictionary<string, string>? segments = null, string? body = null) where T : class;
    }

    public class ApiCaller : IApiCaller
    {
        public readonly RestClient Client;

        public ApiCaller()
        {
            var apiRoot =
                    new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")[
                        "ApiRoot"] ?? throw new InvalidOperationException("ApiRoot configuration is required");
            var options = new RestClientOptions
            {
                BaseUrl = new Uri(apiRoot),
                UseDefaultCredentials = true,
                UserAgent =
                    @"Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.139 Safari/537.36"
            };

            Client = new RestClient(options);
        }

        public ApiResult<T> Call<T>(Endpoints endpoint, Method method, Dictionary<string, string>? segments = null, string? body = null) where T : class
        {
            var result = new ApiResult<T>();
            //prepare request
            var request = new RestRequest(GetEndpointPath(endpoint), method);
            request.AddHeader("Content-Type", "application/json; charset=utf-8");
            if (segments != null)
            {
                foreach (var keyValuePair in segments)
                {
                    request.AddQueryParameter(keyValuePair.Key, keyValuePair.Value);
                }
            }
            if (body != null)
            {
                // For more details see https://restsharp.dev/v107/#body-parameters.
                request.AddStringBody($"{body}", ContentType.Json);
            }
            var response = Client.ExecuteAsync<T>(request).Result;

            string? responseContent = response.Content;
            if (string.IsNullOrEmpty(responseContent))
            {
                return result;
            }
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    result.Value = JsonSerializer.Deserialize<T>(responseContent);
                    result.IsModelValid = true;
                }
                else
                {
                    if (response.ContentType != null
                        && response.ContentType.Contains(ContentType.Json))
                    {
                        ContentResponse? contentResponse = JsonSerializer.Deserialize<ContentResponse>(responseContent);
                        if (contentResponse != null)
                        {
                            result.ErrorMessage = contentResponse.Message;
                        }
                    }
                    else
                    {
                        result.ErrorMessage = responseContent;
                    }
                    result.IsModelValid = false;
                }
            }
            catch (Exception e)
            {
                result.IsModelValid = false;
                result.ErrorMessage = e.Message;
            }
            return result;
        }

        private string GetEndpointPath(Endpoints value)
        {
            string? endpointName = Enum.GetName(typeof(Endpoints), value);
            return endpointName ?? string.Empty;
        }
    }

}
