using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RestSharp;

namespace Tests.Acceptance.Support
{
    public class DorcApiClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly RestClient _client;
        private readonly string? _bearerToken;
        private bool disposedValue;

        public DorcApiClient()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json")
                .AddEnvironmentVariables()
                .Build();

            var configuredDorcBaseUrl = configuration.GetSection("DorcApi")["BaseUrl"];
            string apiRoot = !string.IsNullOrWhiteSpace(configuredDorcBaseUrl)
                ? configuredDorcBaseUrl
                : (configuration.GetSection("AppSettings")["BaseAddress"] ?? string.Empty);

            _bearerToken = AcquireBearerTokenIfConfigured(configuration, apiRoot);

            var options = new RestClientOptions
            {
                BaseUrl = new Uri(apiRoot),
                UseDefaultCredentials = string.IsNullOrEmpty(_bearerToken),
                UserAgent =
                    @"Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.139 Safari/537.36"
            };

            _client = new RestClient(options);
        }

        private static string? AcquireBearerTokenIfConfigured(IConfiguration configuration, string apiRoot)
        {
            var envToken = Environment.GetEnvironmentVariable("DORC_API_BEARER_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                return envToken;
            }

            var dorcApiSection = configuration.GetSection("DorcApi");
            var clientId = dorcApiSection["ClientId"];
            var clientSecret = dorcApiSection["ClientSecret"];
            var authority = dorcApiSection["Authority"];
            var scope = dorcApiSection["Scope"];
            if (string.IsNullOrWhiteSpace(scope))
            {
                scope = "dorc-api.manage";
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(apiRoot))
            {
                authority = ResolveAuthorityFromApiConfig(apiRoot);
            }

            if (string.IsNullOrWhiteSpace(authority))
            {
                return null;
            }

            using var http = new HttpClient();
            var tokenEndpoint = new Uri(authority.TrimEnd('/') + "/connect/token", UriKind.Absolute);
            using var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("scope", scope)
            });

            using var response = http.PostAsync(tokenEndpoint, form).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var tokenPayload = response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions).GetAwaiter().GetResult();
            return tokenPayload?.AccessToken;
        }

        private static string? ResolveAuthorityFromApiConfig(string apiRoot)
        {
            using var http = new HttpClient();
            var baseUri = new Uri(apiRoot.EndsWith('/') ? apiRoot : apiRoot + "/", UriKind.Absolute);
            var apiConfigUri = new Uri(baseUri, "ApiConfig");
            using var response = http.GetAsync(apiConfigUri).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var configRoot = response.Content.ReadFromJsonAsync<ApiConfigResponse>(JsonOptions).GetAwaiter().GetResult();
            return configRoot?.OAuthAuthority;
        }

        public ApiResult<T> Call<T>(Endpoints endpoint, Method method, IEnumerable<string>? segments = null,
            IDictionary<string, string>? queryParameters = null, string? body = null) where T : class
        {
            var result = new ApiResult<T>();

            string? urlString = GetEndpointPath(endpoint);

            if (segments != null)
            {
                foreach (var segment in segments)
                {
                    urlString += "/" + segment;
                }
            }

            var request = new RestRequest(urlString, method);
            if (!string.IsNullOrEmpty(_bearerToken))
            {
                request.AddHeader("Authorization", $"Bearer {_bearerToken}");
            }

            if (queryParameters != null)
                foreach (var keyValuePair in queryParameters)
                    request.AddQueryParameter(keyValuePair.Key, keyValuePair.Value);
            if (body != null)
            {
                // For more details see https://restsharp.dev/v107/#body-parameters.
                request.AddStringBody($"{body}", ContentType.Json);
            }

            var response = _client.Execute(request);
            result.RawJson = response.Content;
            if (response.StatusCode != HttpStatusCode.OK)
                return CreateFailureResult<T>(response.Content);

            if (result.RawJson == null)
            {
                return CreateFailureResult<T>("Content is null.");
            }

            try
            {
                result.IsModelValid = true;
                result.Message = "Model Valid!";
                result.Model = typeof(string) == typeof(T)
                    ? result.RawJson as T
                    : JsonSerializer.Deserialize<T>(result.RawJson, JsonOptions);
            }
            catch (JsonException jsonException)
            {
                result.IsModelValid = false;
                result.Message = jsonException.Message;
            }
            catch (NotSupportedException e)
            {
                return CreateFailureResult<T>(e.Message);
            }
            catch (ArgumentException e)
            {
                return CreateFailureResult<T>(e.Message);
            }

            return result;
        }

        public RestResponse ExecuteRaw(
            string relativePath,
            Method method,
            IDictionary<string, string>? queryParameters = null,
            object? body = null)
        {
            var request = new RestRequest(relativePath, method);
            if (!string.IsNullOrEmpty(_bearerToken))
            {
                request.AddHeader("Authorization", $"Bearer {_bearerToken}");
            }

            if (queryParameters != null)
            {
                foreach (var keyValuePair in queryParameters)
                {
                    request.AddQueryParameter(keyValuePair.Key, keyValuePair.Value);
                }
            }

            if (body != null)
            {
                var jsonBody = body is string rawString
                    ? rawString
                    : JsonSerializer.Serialize(body, JsonOptions);
                request.AddStringBody(jsonBody, ContentType.Json);
            }

            return _client.Execute(request);
        }

        public static T? Deserialize<T>(string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(rawJson, JsonOptions);
        }

        private sealed class ApiConfigResponse
        {
            public string? OAuthAuthority { get; set; }
        }

        private sealed class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }
        }

        private static ApiResult<T> CreateFailureResult<T>(string? msg) where T : class
        {
            return new ApiResult<T> { IsModelValid = false, Message = msg };
        }

        private string? GetEndpointPath(Endpoints value)
        {
            return Enum.GetName(typeof(Endpoints), value);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this._client.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}