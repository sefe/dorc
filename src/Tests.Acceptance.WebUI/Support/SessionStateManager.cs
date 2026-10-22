using System.Text.Json;
using Microsoft.Playwright;

namespace Tests.Acceptance.WebUI.Support
{
    /// <summary>
    /// Saved-session sign-in (mirrors Coral.Testing's LoginStateHelper idea).
    ///
    /// A human signs in ONCE in a visible browser (Entra ID + MFA). We then save:
    ///  - {env}.storageState.json  : cookies + localStorage (Playwright storageState),
    ///                               includes the Entra SSO cookies.
    ///  - {env}.sessionStorage.json: sessionStorage. DOrc's oidc-client-ts keeps the
    ///                               signed-in user ("oidc.user:...") here by default, and
    ///                               Playwright storageState does NOT capture sessionStorage.
    /// Every scenario then starts a fresh browser context pre-loaded with both, so no login.
    ///
    /// The files contain live tokens: they are written under bin/ (never committed) and
    /// reused only while younger than SessionState:CacheTTL.
    /// </summary>
    public static class SessionStateManager
    {
        private const string OidcUserKeyPrefix = "oidc.user:";
        private const string RestoredFlag = "__dorcTestSessionRestored";

        private static string FilePrefix =>
            Path.Combine(TestConfiguration.GetSessionStateDirectory(), TestConfiguration.GetEnvironmentName());

        public static string StorageStatePath => FilePrefix + ".storageState.json";
        public static string SessionStoragePath => FilePrefix + ".sessionStorage.json";

        private static string AppOrigin =>
            new Uri(TestConfiguration.GetBaseUrl()).GetLeftPart(UriPartial.Authority);

        public static bool HasValidSession()
        {
            if (!TestConfiguration.IsSessionStateCachingEnabled())
                return false;

            if (!File.Exists(StorageStatePath) || !File.Exists(SessionStoragePath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(StorageStatePath);
            return age.TotalSeconds < TestConfiguration.GetSessionStateCacheTTL();
        }

        /// <summary>
        /// Opens a visible browser, waits for the user to complete sign-in (incl. MFA), then saves the session.
        /// </summary>
        public static async Task CaptureInteractivelyAsync(IPlaywright playwright)
        {
            if (TestConfiguration.GetHeadless())
            {
                throw new InvalidOperationException(
                    $"No valid saved session found at '{StorageStatePath}'. Sign-in needs a visible browser: " +
                    "set Playwright:Headless=false (e.g. appsettings.Development.json) and run the tests once to sign in.");
            }

            Directory.CreateDirectory(TestConfiguration.GetSessionStateDirectory());

            var browser = await BrowserFactory.LaunchAsync(playwright, headless: false);
            try
            {
                var context = await browser.NewContextAsync(
                    BrowserFactory.WithWindowSize(new BrowserNewContextOptions(), headless: false));
                var page = await context.NewPageAsync();
                await page.GotoAsync(TestConfiguration.GetBaseUrl());

                Console.WriteLine($"[SessionState] Please sign in to DOrc in the opened browser " +
                                  $"(waiting up to {TestConfiguration.GetSignInTimeoutSeconds()}s)...");

                await WaitForSignedInAsync(page);

                await context.StorageStateAsync(new() { Path = StorageStatePath });
                var sessionStorageJson = await page.EvaluateAsync<string>(
                    "() => JSON.stringify(Object.fromEntries(Object.entries(sessionStorage)))");
                await File.WriteAllTextAsync(SessionStoragePath, sessionStorageJson);

                Console.WriteLine($"[SessionState] Session saved to '{StorageStatePath}'.");
            }
            finally
            {
                await browser.CloseAsync();
            }
        }

        /// <summary>
        /// Creates a browser context already signed in, using the saved session files.
        /// </summary>
        public static async Task<IBrowserContext> CreateSignedInContextAsync(IBrowser browser)
        {
            var context = await browser.NewContextAsync(BrowserFactory.WithWindowSize(
                new BrowserNewContextOptions { StorageStatePath = StorageStatePath }, TestConfiguration.GetHeadless()));

            // Restore sessionStorage before any app script runs, once per tab, only on the DOrc origin
            // (so silently renewed tokens are not overwritten on later navigations).
            var entriesJson = await File.ReadAllTextAsync(SessionStoragePath);
            var originJson = JsonSerializer.Serialize(AppOrigin);
            await context.AddInitScriptAsync($@"(() => {{
                if (location.origin !== {originJson} || sessionStorage.getItem('{RestoredFlag}')) return;
                const entries = {entriesJson};
                for (const key in entries) sessionStorage.setItem(key, entries[key]);
                sessionStorage.setItem('{RestoredFlag}', '1');
            }})();");

            return context;
        }

        private static async Task WaitForSignedInAsync(IPage page)
        {
            var deadline = DateTime.UtcNow.AddSeconds(TestConfiguration.GetSignInTimeoutSeconds());

            while (DateTime.UtcNow < deadline)
            {
                await AcceptStaySignedInPromptAsync(page);

                if (page.Url.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var signedIn = await page.EvaluateAsync<bool>(
                            "prefix => Object.keys(sessionStorage).some(k => k.startsWith(prefix))", OidcUserKeyPrefix);
                        if (signedIn)
                            return;
                    }
                    catch (PlaywrightException)
                    {
                        // Page is mid-navigation (redirect back from Entra ID); poll again.
                    }
                }

                await Task.Delay(1000);
            }

            var screenshot = Path.Combine(TestConfiguration.GetSessionStateDirectory(), "signin-timeout.png");
            try { await page.ScreenshotAsync(new() { Path = screenshot }); }
            catch (Exception ex) { screenshot = $"(not captured: {ex.Message})"; }

            throw new TimeoutException(
                $"Sign-in was not completed within {TestConfiguration.GetSignInTimeoutSeconds()}s " +
                $"(no oidc user found in sessionStorage on the DOrc origin). Last URL: {page.Url}. Screenshot: {screenshot}");
        }

        /// <summary>
        /// After MFA, Entra ID shows "Stay signed in?" and waits for a click; answer "Yes" so sign-in completes.
        /// </summary>
        private static async Task AcceptStaySignedInPromptAsync(IPage page)
        {
            if (!page.Url.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (await page.GetByText("Stay signed in?").IsVisibleAsync())
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync(new() { Timeout = 5000 });
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                // Prompt disappeared or page navigated mid-check; next poll will retry.
            }
        }
    }
}
