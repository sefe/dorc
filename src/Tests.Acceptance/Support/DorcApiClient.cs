using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using RestSharp;

namespace Tests.Acceptance.Support
{
    public class DorcApiClient : IDisposable
    {
        private readonly RestClient _client;
        private readonly string? _bearerToken;
        private bool disposedValue;

        public DorcApiClient()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json")
                .AddEnvironmentVariables()
                .Build();

            string apiRoot = configuration.GetSection("DorcApi")["BaseUrl"]
                ?? configuration.GetSection("AppSettings")["BaseAddress"]
                ?? string.Empty;

            _bearerToken = AcquireBearerTokenIfConfigured(configuration);

            var options = new RestClientOptions
            {
                BaseUrl = new Uri(apiRoot),
                UseDefaultCredentials = string.IsNullOrEmpty(_bearerToken),
                UserAgent =
                    @"Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.139 Safari/537.36"
            };

            _client = new RestClient(options);
        }

        private static string? AcquireBearerTokenIfConfigured(IConfiguration configuration)
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
            var scope = dorcApiSection["Scope"] ?? "dorc-api.manage";

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(authority))
            {
                return null;
            }

            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority(new Uri(authority))
                .Build();

            var tokenResult = app.AcquireTokenForClient(new[] { scope }).ExecuteAsync().GetAwaiter().GetResult();
            return tokenResult.AccessToken;
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
                T? model = null;
                if (typeof(string) == typeof(T))
                {
                    model = result.RawJson as T;
                }
                else
                {
                    model = JsonSerializer.Deserialize<T>(result.RawJson);
                }
                result.Model = model;
            }
            catch (JsonException jsonException)
            {
                result.IsModelValid = false;
                result.Message = jsonException.Message;
            }
            catch (Exception e)
            {
                return CreateFailureResult<T>(e.Message);
            }

            return result;
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