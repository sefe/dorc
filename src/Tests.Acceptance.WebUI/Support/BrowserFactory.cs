using Microsoft.Playwright;

namespace Tests.Acceptance.WebUI.Support
{
    /// <summary>
    /// Launches the browser configured in appsettings (Playwright:BrowserType / Args / Timeout).
    /// Shared by the per-scenario hooks and the one-off interactive sign-in.
    /// </summary>
    public static class BrowserFactory
    {
        private const string StartMaximized = "--start-maximized";

        // Headless has no real window to maximise, so emulate a maximised full-HD screen instead.
        private static readonly ViewportSize HeadlessViewport = new() { Width = 1920, Height = 1080 };

        public static Task<IBrowser> LaunchAsync(IPlaywright playwright, bool headless)
        {
            var args = TestConfiguration.GetBrowserArgs().ToList();
            if (!headless && !args.Contains(StartMaximized, StringComparer.OrdinalIgnoreCase))
                args.Add(StartMaximized);
            var channel = TestConfiguration.GetBrowserChannel();
            var options = new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Timeout = TestConfiguration.GetTimeout(),
                Args = args.Count > 0 ? args : null,
                // e.g. "msedge"/"chrome": use the installed, corporate-managed browser instead of
                // Playwright's bundled Chromium (needed for Entra ID device-based sign-in).
                Channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
                // Delay (ms) between actions so a human can watch the run; 0 = full speed.
                SlowMo = TestConfiguration.GetSlowMo()
            };

            return TestConfiguration.GetBrowserType().ToLowerInvariant() switch
            {
                "firefox" => playwright.Firefox.LaunchAsync(options),
                _ => playwright.Chromium.LaunchAsync(options),
            };
        }

        /// <summary>
        /// Sizes the page to the window. Headed: NoViewport, so the page fills the maximised window
        /// (Playwright otherwise forces 1280x720). Headless: a fixed 1920x1080 viewport.
        /// </summary>
        public static BrowserNewContextOptions WithWindowSize(BrowserNewContextOptions options, bool headless)
        {
            options.ViewportSize = headless ? HeadlessViewport : ViewportSize.NoViewport;
            return options;
        }
    }
}
