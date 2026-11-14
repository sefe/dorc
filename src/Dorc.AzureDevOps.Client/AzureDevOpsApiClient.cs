using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Client.Auth;
using Org.OpenAPITools.Model;
using Polly;
using RestSharp;
using RestSharp.Serializers;
using FileIO = System.IO.File;
using OpenApiHttpMethod = Org.OpenAPITools.Client.HttpMethod;
using RestSharpMethod = RestSharp.Method;

namespace Dorc.AzureDevOps.Client
{
    /// <summary>
    /// Non-redirecting <see cref="ApiClient"/> subclass used for Azure DevOps REST calls so an
    /// HTTP 3xx response from an on-premises server cannot silently forward default Windows
    /// credentials (<c>UseDefaultCredentials = true</c>) or bearer tokens to an unvalidated host.
    /// </summary>
    public sealed class AzureDevOpsApiClient : ApiClient, ISynchronousClient, IAsynchronousClient
    {
        private readonly string _baseUrl;
        private readonly Func<bool, HttpMessageHandler> _httpHandlerFactory;

        public AzureDevOpsApiClient(string basePath, Func<bool, HttpMessageHandler> httpHandlerFactory = null)
            : base(basePath)
        {
            _baseUrl = basePath;
            _httpHandlerFactory = httpHandlerFactory;
        }

        internal sealed class AzureDevOpsJsonCodec : IRestSerializer, ISerializer, IDeserializer
        {
            private readonly IReadableConfiguration _configuration;
            private readonly JsonSerializerSettings _serializerSettings;

            public AzureDevOpsJsonCodec(JsonSerializerSettings serializerSettings, IReadableConfiguration configuration)
            {
                _serializerSettings = serializerSettings;
                _configuration = configuration;
            }

            public string Serialize(object obj)
            {
                if (obj is AbstractOpenAPISchema schema)
                {
                    return schema.ToJson();
                }

                return JsonConvert.SerializeObject(obj, _serializerSettings);
            }

            public string Serialize(Parameter bodyParameter) => Serialize(bodyParameter.Value);

            public T Deserialize<T>(RestResponse response) => (T)Deserialize(response, typeof(T));

            private object Deserialize(RestResponse response, Type type)
            {
                if (type == typeof(byte[]))
                {
                    return response.RawBytes;
                }

                if (type == typeof(Stream))
                {
                    var bytes = response.RawBytes ?? Array.Empty<byte>();
                    if (response.Headers != null)
                    {
                        var filePath = string.IsNullOrEmpty(_configuration.TempFolderPath)
                            ? Path.GetTempPath()
                            : _configuration.TempFolderPath;
                        var regex = new Regex(@"Content-Disposition=.*filename=['""]?([^'""\s]+)['""]?$");
                        var match = response.Headers
                            .Select(h => regex.Match(h.ToString()))
                            .FirstOrDefault(m => m.Success);
                        if (match != null)
                        {
                            var sanitizedLeaf = Path.GetFileName(
                                ClientUtils.SanitizeFilename(match.Groups[1].Value.Replace("\"", "").Replace("'", "")));
                            if (!string.IsNullOrEmpty(sanitizedLeaf))
                            {
                                var baseFull = Path.GetFullPath(filePath);
                                var fullPath = Path.GetFullPath(Path.Join(baseFull, sanitizedLeaf));
                                if (!fullPath.StartsWith(baseFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                                    && fullPath != baseFull)
                                {
                                    throw new ArgumentException("Invalid file path");
                                }
                                FileIO.WriteAllBytes(fullPath, bytes);
                                return new FileStream(fullPath, FileMode.Open);
                            }
                        }
                    }

                    return new MemoryStream(bytes);
                }

                if (type.Name.StartsWith("System.Nullable`1[[System.DateTime", StringComparison.Ordinal))
                {
                    return DateTime.Parse(response.Content, null, DateTimeStyles.RoundtripKind);
                }

                if (type == typeof(string) || type.Name.StartsWith("System.Nullable", StringComparison.Ordinal))
                {
                    return Convert.ChangeType(response.Content, type, CultureInfo.InvariantCulture);
                }

                try
                {
                    return JsonConvert.DeserializeObject(response.Content, type, _serializerSettings);
                }
                catch (JsonException e)
                {
                    throw new ApiException(500, e.Message);
                }
            }

            public ISerializer Serializer => this;
            public IDeserializer Deserializer => this;
            public string[] AcceptedContentTypes => ContentType.JsonAccept;

            public SupportsContentType SupportsContentType => contentType =>
                contentType.Value.EndsWith("json", StringComparison.InvariantCultureIgnoreCase) ||
                contentType.Value.EndsWith("javascript", StringComparison.InvariantCultureIgnoreCase);

            public ContentType ContentType { get; set; } = ContentType.Json;
            public DataFormat DataFormat => DataFormat.Json;
        }

        private static RestSharpMethod MapMethod(OpenApiHttpMethod method)
        {
            return method switch
            {
                OpenApiHttpMethod.Get => RestSharpMethod.Get,
                OpenApiHttpMethod.Post => RestSharpMethod.Post,
                OpenApiHttpMethod.Put => RestSharpMethod.Put,
                OpenApiHttpMethod.Delete => RestSharpMethod.Delete,
                OpenApiHttpMethod.Head => RestSharpMethod.Head,
                OpenApiHttpMethod.Options => RestSharpMethod.Options,
                OpenApiHttpMethod.Patch => RestSharpMethod.Patch,
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
            };
        }

        private static RestRequest NewRequest(
            OpenApiHttpMethod method,
            string path,
            RequestOptions options,
            IReadableConfiguration configuration)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var request = new RestRequest(path, MapMethod(method));

            if (options.PathParameters != null)
            {
                foreach (var pathParam in options.PathParameters)
                {
                    request.AddParameter(pathParam.Key, pathParam.Value, ParameterType.UrlSegment);
                }
            }

            if (options.QueryParameters != null)
            {
                foreach (var queryParam in options.QueryParameters)
                {
                    foreach (var value in queryParam.Value)
                    {
                        request.AddQueryParameter(queryParam.Key, value);
                    }
                }
            }

            if (configuration.DefaultHeaders != null)
            {
                foreach (var headerParam in configuration.DefaultHeaders)
                {
                    request.AddHeader(headerParam.Key, headerParam.Value);
                }
            }

            if (options.HeaderParameters != null)
            {
                foreach (var headerParam in options.HeaderParameters)
                {
                    foreach (var value in headerParam.Value)
                    {
                        request.AddOrUpdateHeader(headerParam.Key, value);
                    }
                }
            }

            if (options.FormParameters != null)
            {
                foreach (var formParam in options.FormParameters)
                {
                    request.AddParameter(formParam.Key, formParam.Value);
                }
            }

            if (options.Data != null)
            {
                if (options.Data is Stream stream)
                {
                    var contentType = "application/octet-stream";
                    if (options.HeaderParameters != null && options.HeaderParameters.TryGetValue("Content-Type", out var streamContentTypes) && streamContentTypes.Count > 0)
                    {
                        contentType = streamContentTypes[0];
                    }

                    var bytes = ClientUtils.ReadAsBytes(stream);
                    request.AddParameter(contentType, bytes, ParameterType.RequestBody);
                }
                else
                {
                    request.RequestFormat = DataFormat.Json;
                    request.AddJsonBody(options.Data);
                }
            }

            if (options.FileParameters != null)
            {
                foreach (var fileParam in options.FileParameters)
                {
                    foreach (var file in fileParam.Value)
                    {
                        var bytes = ClientUtils.ReadAsBytes(file);
                        if (file is FileStream fileStream)
                        {
                            request.AddFile(fileParam.Key, bytes, Path.GetFileName(fileStream.Name));
                        }
                        else
                        {
                            request.AddFile(fileParam.Key, bytes, "no_file_name_provided");
                        }
                    }
                }
            }

            if (options.HeaderParameters != null
                && options.HeaderParameters.TryGetValue("Content-Type", out var contentTypes)
                && contentTypes.Any(header => header.Contains("multipart/form-data")))
            {
                request.AlwaysMultipartFormData = true;
            }

            return request;
        }

        private static ApiResponse<T> ToApiResponse<T>(RestResponse<T> response)
        {
            var transformed = new ApiResponse<T>(response.StatusCode, new Multimap<string, string>(), response.Data, response.Content)
            {
                ErrorText = response.ErrorMessage,
                Cookies = new List<Cookie>()
            };

            if (response.Headers != null)
            {
                foreach (var responseHeader in response.Headers)
                {
                    transformed.Headers.Add(responseHeader.Name, ClientUtils.ParameterToString(responseHeader.Value));
                }
            }

            if (response.ContentHeaders != null)
            {
                foreach (var responseHeader in response.ContentHeaders)
                {
                    transformed.Headers.Add(responseHeader.Name, ClientUtils.ParameterToString(responseHeader.Value));
                }
            }

            if (response.Cookies != null)
            {
                foreach (var cookie in response.Cookies.Cast<Cookie>())
                {
                    transformed.Cookies.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
                }
            }

            return transformed;
        }

        private async Task<ApiResponse<T>> ExecClientAsync<T>(
            Func<RestClient, Task<RestResponse<T>>> getResponse,
            Action<RestClientOptions> setOptions,
            RestRequest request,
            RequestOptions options,
            IReadableConfiguration configuration)
        {
            var baseUrl = configuration.GetOperationServerUrl(options.Operation, options.OperationIndex) ?? _baseUrl;
            var clientOptions = new RestClientOptions(baseUrl)
            {
                ClientCertificates = configuration.ClientCertificates,
                Timeout = configuration.Timeout,
                Proxy = configuration.Proxy,
                UserAgent = configuration.UserAgent,
                UseDefaultCredentials = configuration.UseDefaultCredentials,
                RemoteCertificateValidationCallback = configuration.RemoteCertificateValidationCallback,
                FollowRedirects = false,
                ConfigureMessageHandler = existingHandler =>
                {
                    if (_httpHandlerFactory != null)
                    {
                        return _httpHandlerFactory(configuration.UseDefaultCredentials);
                    }

                    if (existingHandler is HttpClientHandler httpClientHandler)
                    {
                        httpClientHandler.AllowAutoRedirect = false;
                        httpClientHandler.UseDefaultCredentials = configuration.UseDefaultCredentials;
                        return httpClientHandler;
                    }

                    return new HttpClientHandler
                    {
                        AllowAutoRedirect = false,
                        UseDefaultCredentials = configuration.UseDefaultCredentials
                    };
                }
            };
            setOptions(clientOptions);

            if (!string.IsNullOrEmpty(configuration.OAuthTokenUrl) &&
                !string.IsNullOrEmpty(configuration.OAuthClientId) &&
                !string.IsNullOrEmpty(configuration.OAuthClientSecret) &&
                configuration.OAuthFlow != null)
            {
                clientOptions.Authenticator = new OAuthAuthenticator(
                    configuration.OAuthTokenUrl,
                    configuration.OAuthClientId,
                    configuration.OAuthClientSecret,
                    configuration.OAuthScope,
                    configuration.OAuthFlow,
                    SerializerSettings,
                    configuration);
            }

            using var client = new RestClient(
                clientOptions,
                configureSerialization: serializerConfig =>
                    serializerConfig.UseSerializer(() => new AzureDevOpsJsonCodec(SerializerSettings, configuration)));

            var response = await getResponse(client).ConfigureAwait(false);

            if (typeof(AbstractOpenAPISchema).IsAssignableFrom(typeof(T)))
            {
                try
                {
                    response.Data = (T)typeof(T).GetMethod("FromJson").Invoke(null, new object[] { response.Content });
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                    {
                        throw ex.InnerException;
                    }

                    throw;
                }
            }
            else if (typeof(T).Name == "Stream")
            {
                response.Data ??= (T)(object)new MemoryStream(response.RawBytes ?? Array.Empty<byte>());
            }
            else if (typeof(T).Name == "Byte[]")
            {
                response.Data = (T)(object)response.RawBytes;
            }
            else if (typeof(T).Name == "String")
            {
                response.Data = (T)(object)response.Content;
            }

            var result = ToApiResponse(response);
            if (response.ErrorMessage != null)
            {
                result.ErrorText = response.ErrorMessage;
            }

            return result;
        }

        private static async Task<RestResponse<T>> DeserializeRestResponseFromPolicyAsync<T>(
            RestClient client,
            RestRequest request,
            PolicyResult<RestResponse> policyResult,
            CancellationToken cancellationToken = default)
        {
            if (policyResult.Outcome == OutcomeType.Successful)
            {
                return await client.Deserialize<T>(policyResult.Result, cancellationToken).ConfigureAwait(false);
            }

            return new RestResponse<T>(request)
            {
                ErrorException = policyResult.FinalException
            };
        }

        private ApiResponse<T> Exec<T>(RestRequest request, RequestOptions options, IReadableConfiguration configuration)
        {
            Action<RestClientOptions> setOptions = clientOptions =>
            {
                var cookies = new CookieContainer();
                if (options.Cookies != null && options.Cookies.Count > 0)
                {
                    foreach (var cookie in options.Cookies)
                    {
                        cookies.Add(new Cookie(cookie.Name, cookie.Value));
                    }
                }

                clientOptions.CookieContainer = cookies;
            };

            Func<RestClient, Task<RestResponse<T>>> getResponse = client =>
            {
                if (RetryConfiguration.RetryPolicy != null)
                {
                    var policyResult = RetryConfiguration.RetryPolicy.ExecuteAndCapture(() => client.Execute(request));
                    return DeserializeRestResponseFromPolicyAsync<T>(client, request, policyResult);
                }

                return Task.FromResult(client.Execute<T>(request));
            };

            return ExecClientAsync(getResponse, setOptions, request, options, configuration).GetAwaiter().GetResult();
        }

        private Task<ApiResponse<T>> ExecAsync<T>(
            RestRequest request,
            RequestOptions options,
            IReadableConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Action<RestClientOptions> setOptions = _ => { };

            Func<RestClient, Task<RestResponse<T>>> getResponse = async client =>
            {
                if (RetryConfiguration.AsyncRetryPolicy != null)
                {
                    var policyResult = await RetryConfiguration.AsyncRetryPolicy
                        .ExecuteAndCaptureAsync(ct => client.ExecuteAsync(request, ct), cancellationToken)
                        .ConfigureAwait(false);
                    return await DeserializeRestResponseFromPolicyAsync<T>(client, request, policyResult, cancellationToken)
                        .ConfigureAwait(false);
                }

                return await client.ExecuteAsync<T>(request, cancellationToken).ConfigureAwait(false);
            };

            return ExecClientAsync(getResponse, setOptions, request, options, configuration);
        }

        Task<ApiResponse<T>> IAsynchronousClient.GetAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Get, path, options, config), options, config, cancellationToken);
        }

        Task<ApiResponse<T>> IAsynchronousClient.PostAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Post, path, options, config), options, config, cancellationToken);
        }

        Task<ApiResponse<T>> IAsynchronousClient.PutAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Put, path, options, config), options, config, cancellationToken);
        }

        Task<ApiResponse<T>> IAsynchronousClient.DeleteAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Delete, path, options, config), options, config, cancellationToken);
        }

        Task<ApiResponse<T>> IAsynchronousClient.HeadAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Head, path, options, config), options, config, cancellationToken);
        }

        Task<ApiResponse<T>> IAsynchronousClient.OptionsAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Options, path, options, config), options, config, cancellationToken);
        }

        Task<ApiResponse<T>> IAsynchronousClient.PatchAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration, CancellationToken cancellationToken)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return ExecAsync<T>(NewRequest(OpenApiHttpMethod.Patch, path, options, config), options, config, cancellationToken);
        }

        ApiResponse<T> ISynchronousClient.Get<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Get, path, options, config), options, config);
        }

        ApiResponse<T> ISynchronousClient.Post<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Post, path, options, config), options, config);
        }

        ApiResponse<T> ISynchronousClient.Put<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Put, path, options, config), options, config);
        }

        ApiResponse<T> ISynchronousClient.Delete<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Delete, path, options, config), options, config);
        }

        ApiResponse<T> ISynchronousClient.Head<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Head, path, options, config), options, config);
        }

        ApiResponse<T> ISynchronousClient.Options<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Options, path, options, config), options, config);
        }

        ApiResponse<T> ISynchronousClient.Patch<T>(string path, RequestOptions options, IReadableConfiguration configuration)
        {
            var config = configuration ?? GlobalConfiguration.Instance;
            return Exec<T>(NewRequest(OpenApiHttpMethod.Patch, path, options, config), options, config);
        }
    }
}
