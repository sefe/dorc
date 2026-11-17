using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Tools.RequestCLI
{
    internal class Program
    {
        internal static Func<IConfigurationRoot> ConfigurationFactory { get; set; } =
            () => new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

        internal static Func<IConfigurationRoot, IApiCaller> ApiCallerFactory { get; set; } =
            config => new ApiCaller(new DorcOAuthClientConfiguration(config));

        internal static Func<IOAuthClientConfiguration, string?> TokenProviderFactory { get; set; } =
            oauthConfig => new DorcApiTokenProvider(oauthConfig).GetTokenAsync().GetAwaiter().GetResult();

        internal static HttpClient CreateAuthenticatedHttpClient(
            IConfigurationRoot config,
            HttpMessageHandler? innerHandler = null)
        {
            var baseUrl = (config["DorcApi:BaseUrl"] ?? "https://localhost").TrimEnd('/') + "/";

            if (!string.IsNullOrWhiteSpace(config["DorcApi:ClientId"]) &&
                !string.IsNullOrWhiteSpace(config["DorcApi:ClientSecret"]))
            {
                var oauthConfig = new DorcOAuthClientConfiguration(config);
                var refreshingHandler = new BearerTokenRefreshingHandler(
                    oauthConfig,
                    TokenProviderFactory,
                    ErrorOutput,
                    innerHandler);
                var httpClient = new HttpClient(refreshingHandler) { BaseAddress = new Uri(baseUrl) };
                var initialToken = refreshingHandler.TryAcquireToken();
                if (!string.IsNullOrWhiteSpace(initialToken))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", initialToken);
                }

                return httpClient;
            }

            return innerHandler is not null
                ? new HttpClient(innerHandler) { BaseAddress = new Uri(baseUrl) }
                : new HttpClient(new HttpClientHandler { UseDefaultCredentials = true }) { BaseAddress = new Uri(baseUrl) };
        }

        internal static void ErrorOutput(string strText)
        {
            Console.Error.WriteLine(DateTime.Now + " - " + strText);
        }

        internal static Func<IConfigurationRoot, Action<string>, IEphemeralEnvironmentCliClient> EphemeralClientFactory { get; set; } =
            (config, output) => new EphemeralEnvironmentCliClient(CreateAuthenticatedHttpClient(config), output);

        internal static Func<bool> IsUserInteractive { get; set; } = () => Environment.UserInteractive;
        internal static Action ReadKeyAction { get; set; } = () => Console.ReadKey();

        internal static int Main(string[] args)
        {
            int status = 0;
            AppDomain.CurrentDomain.UnhandledException += CatchAllUnhandledExceptions;

            var config = ConfigurationFactory();

            Output("=== Execution started ===");

            if (EphemeralCliArgs.IsEphemeralCommand(args))
            {
                try
                {
                    var ephemeralArgs = new EphemeralCliArgs(args);
                    var ephemeralClient = EphemeralClientFactory(config, Output);
                    status = ephemeralClient.ExecuteAsync(ephemeralArgs).GetAwaiter().GetResult();
                    Output("=== Execution finished ===");
                    return status;
                }
                catch (Exception e)
                {
                    DisplayExceptionMessage(e);
                    return 1;
                }
            }

            try
            {
                var cliArgs = new CliArgs(args);
                var api = ApiCallerFactory(config);

                Output("============================= Requesting Deploy =============================");
                Output("=== Project            : " + cliArgs.Request.Project);
                Output("=== Target Environment : " + cliArgs.Request.Environment);
                Output("=== Build Text         : " + cliArgs.Request.BuildText);
                Output("=== Build Number       : " + cliArgs.Request.BuildNum);
                Output("=== Build Uri          : " + cliArgs.Request.BuildUrl);
                Output("=== Components         : " + string.Join(" ", cliArgs.Request.Components.ToArray()));
                Output("=== Pinned             : " + cliArgs.Request.Pinned);
                Output("=== Wait               : " + cliArgs.Wait);
                Output("=== Api Root Url       : " + config["DorcApi:BaseUrl"]);
                Output("=============================================================================");

                var result = api.Call<RequestStatusDto>(Endpoints.Request, Method.Post, null, cliArgs.ToString());
                if (!result.IsModelValid || result.Value.Id <= 0)
                {
                    Output("Error creating request");
                    Output(result.ErrorMessage);
                    return 1;
                }

                Output($"Request {result.Value.Id} created");
                if (cliArgs.Wait)
                {
                    var monitor = new RequestMonitor(api);
                    monitor.OnRequestStatusChanged += Output;
                    status = monitor.MonitorRequest(result.Value.Id);
                }
            }
            catch (Exception e)
            {
                DisplayExceptionMessage(e);
                return 1;
            }
            finally
            {
                if (IsUserInteractive())
                {
                    ReadKeyAction();
                }
            }
            Output("=== Execution finished ===");
            return status;
        }

        internal static void CatchAllUnhandledExceptions(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            DisplayExceptionMessage(ex);
        }

        internal static void DisplayExceptionMessage(Exception ex)
        {
            Output($"Exception was caught, message:\n{ex.Message}");
            if (ex.InnerException != null)
            {
                DisplayExceptionMessage(ex.InnerException);
            }
        }

        internal static void Output(string strText)
        {
            Console.WriteLine(DateTime.Now + " - " + strText);
        }
    }

    internal sealed class BearerTokenRefreshingHandler : DelegatingHandler
    {
        private readonly IOAuthClientConfiguration _oauthConfig;
        private readonly Func<IOAuthClientConfiguration, string?> _tokenProvider;
        private readonly Action<string> _errorLogger;
        private string? _cachedToken;

        public BearerTokenRefreshingHandler(
            IOAuthClientConfiguration oauthConfig,
            Func<IOAuthClientConfiguration, string?> tokenProvider,
            Action<string>? errorLogger = null,
            HttpMessageHandler? innerHandler = null)
            : base(innerHandler ?? new HttpClientHandler())
        {
            _oauthConfig = oauthConfig ?? throw new ArgumentNullException(nameof(oauthConfig));
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            _errorLogger = errorLogger ?? Program.ErrorOutput;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_cachedToken))
            {
                _cachedToken = TryAcquireToken();
            }

            if (!string.IsNullOrWhiteSpace(_cachedToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var refreshed = TryAcquireToken();
                if (!string.IsNullOrWhiteSpace(refreshed))
                {
                    _cachedToken = refreshed;
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
                    response.Dispose();
                    response = await base.SendAsync(request, cancellationToken);
                }
            }

            return response;
        }

        internal string? TryAcquireToken()
        {
            try
            {
                var token = _tokenProvider(_oauthConfig);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _cachedToken = token;
                }
                return token;
            }
            catch (Exception ex)
            {
                _errorLogger($"Failed to acquire OAuth bearer token: {ex.Message}");
                return null;
            }
        }
    }
}