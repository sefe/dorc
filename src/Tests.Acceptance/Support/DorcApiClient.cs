using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dorc.ApiModel;
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

        public string ApiRoot { get; }
        public string WebUiRoot { get; }
        public string? BearerToken => _bearerToken;

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
            ApiRoot = apiRoot;
            WebUiRoot = ResolveWebUiRoot(configuration, apiRoot);

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

        private static string ResolveWebUiRoot(IConfiguration configuration, string apiRoot)
        {
            var configuredWebUrl = Environment.GetEnvironmentVariable("DORC_WEB_BASE_URL")
                ?? configuration.GetSection("DorcApi")["WebUiBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configuredWebUrl))
            {
                return configuredWebUrl.TrimEnd('/');
            }

            if (Uri.TryCreate(apiRoot, UriKind.Absolute, out var apiUri))
            {
                return $"{apiUri.Scheme}://{apiUri.Host}";
            }

            return string.Empty;
        }

        public ApiConfigModel? GetApiConfig()
        {
            var response = ExecuteRaw("/ApiConfig", Method.Get);
            return response.IsSuccessful ? Deserialize<ApiConfigModel>(response.Content) : null;
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