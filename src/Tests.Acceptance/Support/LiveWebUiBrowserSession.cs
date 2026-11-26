using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace Tests.Acceptance.Support
{
    public sealed class LiveWebUiBrowserSession : IAsyncDisposable, IDisposable
    {
        private static readonly string[] SystemBrowserCandidates =
        [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        ];

        private static bool _chromiumInstalled;
        private static readonly object InstallLock = new();

        private readonly DorcApiClient _apiClient;
        private readonly ConcurrentQueue<string> _pageErrors = new();
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;
        private bool _appBootstrapped;
        private bool _disposed;

        public string WebUiBaseUrl { get; private set; } = string.Empty;

        public IPage Page => _page ?? throw new InvalidOperationException("LiveWebUiBrowserSession has not been started.");

        public IReadOnlyCollection<string> PageErrors => _pageErrors.ToArray();

        public LiveWebUiBrowserSession(DorcApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        public async Task EnsureStartedAsync()
        {
            if (_page != null)
            {
                return;
            }

            WebUiBaseUrl = ResolveReachableWebUiUrl();
            var apiConfig = _apiClient.GetApiConfig();
            var bearerToken = _apiClient.BearerToken ?? string.Empty;
            var authority = (apiConfig?.OAuthAuthority ?? string.Empty).Trim();
            var clientId = (apiConfig?.OAuthUiClientId ?? string.Empty).Trim();
            var scopes = string.IsNullOrWhiteSpace(apiConfig?.OAuthUiRequestedScopes)
                ? "dorc-api.manage openid profile email"
                : apiConfig!.OAuthUiRequestedScopes;

            var oidcUserJson = JsonSerializer.Serialize(new
            {
                access_token = bearerToken,
                token_type = "Bearer",
                scope = scopes,
                expires_at = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds(),
                profile = new
                {
                    sub = "dorc-acceptance-runner",
                    name = "DOrc Acceptance Runner",
                    preferred_username = "dorc-acceptance-runner",
                    email = "dorc-acceptance-runner@sefe.eu"
                }
            });

            _playwright = await Playwright.CreateAsync();
            _browser = await LaunchBrowserAsync(_playwright);
            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
            });

            var escapedUserJson = JsonSerializer.Serialize(oidcUserJson);
            var exactStorageKey = $"oidc.user:{authority}:{clientId}";
            var trimmedStorageKey = $"oidc.user:{authority.TrimEnd('/')}:{clientId}";
            var escapedExactKey = JsonSerializer.Serialize(exactStorageKey);
            var escapedTrimmedKey = JsonSerializer.Serialize(trimmedStorageKey);

            var initScript = $$"""
                (() => {
                    const userJson = {{escapedUserJson}};
                    const exactKey = {{escapedExactKey}};
                    const trimmedKey = {{escapedTrimmedKey}};
                    try {
                        window.sessionStorage.setItem(exactKey, userJson);
                        window.sessionStorage.setItem(trimmedKey, userJson);
                        window.localStorage.setItem(exactKey, userJson);
                        window.localStorage.setItem(trimmedKey, userJson);
                    } catch (_) {}

                    const origGetItem = Storage.prototype.getItem;
                    Storage.prototype.getItem = function(key) {
                        const existing = origGetItem.call(this, key);
                        if (existing !== null) {
                            return existing;
                        }
                        if (typeof key === 'string' && key.startsWith('oidc.user:')) {
                            return userJson;
                        }
                        return null;
                    };
                })();
                """;

            await _context.AddInitScriptAsync(initScript);
            _page = await _context.NewPageAsync();
            _page.PageError += (_, error) => _pageErrors.Enqueue(error);
        }

        public async Task<ILocator> NavigateToRouteAsync(
            string routePath,
            string expectedSelector,
            float timeoutMs = 45000)
        {
            await EnsureStartedAsync();

            var normalizedPath = routePath.StartsWith('/') ? routePath : "/" + routePath;
            if (!_appBootstrapped)
            {
                await Page.GotoAsync(
                    WebUiBaseUrl,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = timeoutMs
                    });

                await Page.Locator("dorc-app").WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = timeoutMs
                });

                var apiConfig = _apiClient.GetApiConfig();
                var authority = (apiConfig?.OAuthAuthority ?? string.Empty).Trim().TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(authority))
                {
                    var discoveryUrl = $"{authority}/.well-known/openid-configuration";
                    var escapedDiscoveryUrl = JsonSerializer.Serialize(discoveryUrl);
                    var oidcCheckResult = await Page.EvaluateAsync<string>($$"""
                        async () => {
                            const url = {{escapedDiscoveryUrl}};
                            try {
                                const resp = await fetch(url, { mode: 'cors' });
                                if (!resp.ok) {
                                    return `HTTP_${resp.status}`;
                                }
                                const json = await resp.json();
                                return json && json.authorization_endpoint ? 'OK' : 'MISSING_AUTH_ENDPOINT';
                            } catch (err) {
                                return `CORS_OR_FETCH_ERROR:${err && err.message ? err.message : String(err)}`;
                            }
                        }
                        """);
                    if (!string.Equals(oidcCheckResult, "OK", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Browser OIDC discovery check from Web UI origin '{WebUiBaseUrl}' to '{discoveryUrl}' failed with '{oidcCheckResult}'. " +
                            $"Ensure '{WebUiBaseUrl}' is registered in AllowedCorsOrigins on '{authority}'.");
                    }
                }

                _appBootstrapped = true;
            }

            if (normalizedPath != "/")
            {
                var escapedPath = JsonSerializer.Serialize(normalizedPath);
                await Page.EvaluateAsync($$"""
                    () => {
                        const targetPath = {{escapedPath}};
                        window.history.pushState(null, '', targetPath);
                        window.dispatchEvent(new PopStateEvent('popstate'));
                    }
                    """);
            }

            var locator = Page.Locator(expectedSelector).First;
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = timeoutMs
            });

            return locator;
        }

        private string ResolveReachableWebUiUrl()
        {
            return !string.IsNullOrWhiteSpace(_apiClient.WebUiRoot)
                ? _apiClient.WebUiRoot.TrimEnd('/')
                : "https://localhost";
        }

        private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
        {
            var launchArgs = new[]
            {
                "--no-sandbox",
                "--disable-gpu",
                "--ignore-certificate-errors",
                "--allow-insecure-localhost"
            };

            foreach (var candidatePath in SystemBrowserCandidates)
            {
                if (File.Exists(candidatePath))
                {
                    return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        ExecutablePath = candidatePath,
                        Args = launchArgs
                    });
                }
            }

            EnsurePlaywrightChromiumInstalled();
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = launchArgs
            });
        }

        private static void EnsurePlaywrightChromiumInstalled()
        {
            if (_chromiumInstalled)
            {
                return;
            }

            lock (InstallLock)
            {
                if (_chromiumInstalled)
                {
                    return;
                }

                Microsoft.Playwright.Program.Main(["install", "chromium"]);
                _chromiumInstalled = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_page != null)
            {
                await _page.CloseAsync();
            }

            if (_context != null)
            {
                await _context.CloseAsync();
            }

            if (_browser != null)
            {
                await _browser.CloseAsync();
            }

            _playwright?.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
