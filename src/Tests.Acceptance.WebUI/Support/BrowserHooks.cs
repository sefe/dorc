using Reqnroll;
using Microsoft.Playwright;
using Tests.Acceptance.WebUI.Pages;
using Tests.Acceptance.WebUI.Pages.Components;

namespace Tests.Acceptance.WebUI.Support
{
    [Binding]
    public class BrowserHooks
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;

        /// <summary>
        /// Once per test run: make sure a valid saved session exists.
        /// If missing/expired, a visible browser opens for a one-off manual sign-in (Entra ID + MFA).
        /// </summary>
        [BeforeTestRun]
        public static async Task EnsureSignedInSession()
        {
            if (TestConfiguration.IsWinAuthMode())
            {
                Console.WriteLine("[Auth] WinAuth mode - browser uses Windows credentials; saved-session sign-in skipped.");
                return;
            }

            if (SessionStateManager.HasValidSession())
            {
                Console.WriteLine($"[SessionState] Reusing saved session '{SessionStateManager.StorageStatePath}'.");
                return;
            }

            using var playwright = await Playwright.CreateAsync();
            await SessionStateManager.CaptureInteractivelyAsync(playwright);
        }

        /// <summary>
        /// Per scenario: fresh browser + signed-in context (from the saved session) + Page Objects.
        /// Browser type / headless / timeout / args come from appsettings via TestConfiguration.
        /// Page Objects are registered in the scenario container and injected into step definitions.
        /// </summary>
        [BeforeScenario]
        public async Task BeforeScenario(ScenarioContext scenarioContext)
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await BrowserFactory.LaunchAsync(_playwright, TestConfiguration.GetHeadless());
            _context = TestConfiguration.IsWinAuthMode()
                ? await _browser.NewContextAsync(BrowserFactory.WithWindowSize(
                    new BrowserNewContextOptions { IgnoreHTTPSErrors = true }, TestConfiguration.GetHeadless()))
                : await SessionStateManager.CreateSignedInContextAsync(_browser);
            _page = await _context.NewPageAsync();
            _page.SetDefaultTimeout(TestConfiguration.GetTimeout());

            scenarioContext.ScenarioContainer.RegisterInstanceAs<IPage>(_page);
            scenarioContext.ScenarioContainer.RegisterInstanceAs(new HomePage(_page));
            scenarioContext.ScenarioContainer.RegisterInstanceAs(new EnvironmentsPage(_page));
            scenarioContext.ScenarioContainer.RegisterInstanceAs(new EnvironmentVariablesPage(_page));
            scenarioContext.ScenarioContainer.RegisterInstanceAs(new VariablesAuditPage(_page));

            // Shared components (side bar, notifications) used by several pages' step definitions
            scenarioContext.ScenarioContainer.RegisterInstanceAs(new SideNavigation(_page));
            scenarioContext.ScenarioContainer.RegisterInstanceAs(new AppNotification(_page));

            if (TestConfiguration.IsParallelExecutionEnabled())
            {
                Console.WriteLine($"[Parallel Scenario] {scenarioContext.ScenarioInfo.Title}");
            }
        }

        [AfterScenario]
        public async Task AfterScenario()
        {
            if (_context != null)
                await _context.CloseAsync();

            if (_browser != null)
                await _browser.CloseAsync();

            _playwright?.Dispose();
        }
    }
}
