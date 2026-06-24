using Dorc.Core.Configuration;
using RestSharp;
using RestSharp.Authenticators.OAuth2;
using System.Net;
using System.Text.Json;

namespace Dorc.Core
{
    public interface IApiCaller
    {
        ApiResult<T> Call<T>(Endpoints endpoint, Method method, Dictionary<string, string> segments = null, string body = null) where T : class;
    }

    public class ApiCaller : IApiCaller
    {
        private RestClient Client;

        private readonly IOAuthClientConfiguration _configuration;

        public ApiCaller(IOAuthClientConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ApiResult<T> Call<T>(Endpoints endpoint, Method method, Dictionary<string, string>? segments = null, string? body = null) where T : class
        {
            EnsureClientCreatedAuthenticated();

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
                    result.IsModelValid = false;
                    result.ErrorMessage = ExtractErrorMessage(responseContent);
                }
            }
            catch (Exception e)
            {
                result.IsModelValid = false;
                result.ErrorMessage = e.Message;
            }
            return result;
        }

        // Property names, in priority order, that may carry a human-readable error message:
        // "Message" (CopyEnvBuildResponseDto, serialized exceptions), "detail"/"title"
        // (RFC 7807 ProblemDetails / ASP.NET validation responses).
        private static readonly string[] ErrorMessagePropertyNames =
            { "Message", "message", "detail", "title" };

        /// <summary>
        /// Produces a human-readable error message from a failed response body. Handles the
        /// shapes the API emits: a JSON object carrying a message property (see
        /// <see cref="ErrorMessagePropertyNames"/>), a bare JSON string (e.g. BadRequest("...")),
        /// and plain text. Falls back to the raw content when the body is not JSON or has no
        /// recognisable, non-empty message.
        /// </summary>
        private static string ExtractErrorMessage(string responseContent)
        {
            try
            {
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.String)
                {
                    var value = root.GetString();
                    return string.IsNullOrEmpty(value) ? responseContent : value;
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propertyName in ErrorMessagePropertyNames)
                    {
                        if (root.TryGetProperty(propertyName, out var message)
                            && message.ValueKind == JsonValueKind.String)
                        {
                            var value = message.GetString();
                            if (!string.IsNullOrEmpty(value))
                            {
                                return value;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON - fall through and surface the raw content.
            }

            return responseContent;
        }

        private void EnsureClientCreatedAuthenticated()
        {
            if (Client != null)
            {
                return;
            }

            var _tokenProvider = new DorcApiTokenProvider(_configuration);
            var token = _tokenProvider.GetTokenAsync().Result;
            var options = new RestClientOptions
            {
                BaseUrl = new Uri(_configuration.BaseUrl),
                Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(token, "Bearer"),
                UserAgent =
                    @"Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.139 Safari/537.36"
            };

            Client = new RestClient(options);
        }

        private static readonly Dictionary<Endpoints, string> EndpointPaths = new()
        {
            { Endpoints.Properties, "Properties" },
            { Endpoints.PropertyValues, "PropertyValues" },
            { Endpoints.Request, "Request" },
            { Endpoints.ConfigValues, "ConfigValues" },
            { Endpoints.CopyEnvBuild, "CopyEnvBuild" },
            { Endpoints.RefDataEnvironments, "RefDataEnvironments" },
            { Endpoints.RefDataDatabases, "RefDataDatabases" },
            { Endpoints.RefDataDatabasesByType, "RefDataDatabases/ByType" },
            { Endpoints.RefDataServers, "RefDataServers" },
            { Endpoints.RefDataServersAppServersByEnvName, "RefDataServers/AppServersByEnvName" },
            { Endpoints.RefDataSqlPorts, "RefDataSqlPorts" },
            { Endpoints.RefDataSqlPortsByInstance, "RefDataSqlPorts/ByInstance" },
            { Endpoints.RefDataEnvironmentsHistory, "RefDataEnvironmentsHistory" },
        };

        private string GetEndpointPath(Endpoints value)
        {
            return EndpointPaths.TryGetValue(value, out var path) ? path : (Enum.GetName(typeof(Endpoints), value) ?? string.Empty);
        }
    }

}
