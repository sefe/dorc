using Microsoft.Playwright;
using Tests.Acceptance.WebUI.Support;

namespace Tests.Acceptance.WebUI.Pages
{
    /// <summary>
    /// HomePage - Encapsulates all homepage interactions and assertions.
    /// Follows Coral.Testing Page Object pattern for maintainability and reusability.
    /// 
    /// LOCATOR STRATEGY:
    /// This project uses role-based locators (getByRole, getByLabel) instead of ID selectors.
    /// WHY: DOrc UI is built with Lit 3 + Vaadin Web Components, which use Shadow DOM.
    /// Shadow DOM requires locators that pierce across component boundaries.
    /// - getByRole() and getByLabel() reliably work across shadow DOM levels
    /// - id= selectors often fail because IDs do not pierce shadow DOM
    /// - This approach was validated in a local spike against real DOrc components
    /// 
    /// BASE URL CONFIGURATION:
    /// The base URL is no longer hardcoded. It is resolved from appsettings.json
    /// based on the target environment (Development, QA, Staging, Production).
    /// Environment-specific overrides are supported via appsettings.{Environment}.json.
    /// </summary>
    public class HomePage
    {
        private readonly IPage _page;
        private readonly string _baseUrl;

        // Locators: defined once here so selectors live in a single place.
        // Playwright locators are lazy — the DOM is queried only when an action runs,
        // so expression-bodied properties never go stale.
        // Header badge (title="DevOps Orchestrator") - unique; plain "DOrc DV 01" text also matches the footer version.
        private ILocator AppHeading => _page.GetByTitle("DevOps Orchestrator");
        private ILocator EnvironmentsMenuLink => _page.GetByRole(AriaRole.Link, new() { Name = "Environments", Exact = true });

        public HomePage(IPage page)
        {
            _page = page;
            _baseUrl = TestConfiguration.GetBaseUrl();
        }

        /// <summary>
        /// Navigate to the application home page
        /// </summary>
        public async Task LaunchApplication()
        {
            await _page.GotoAsync(_baseUrl);
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        /// <summary>
        /// Waits for the header badge; returns false (instead of throwing) if it doesn't appear,
        /// so the step definition can assert with a readable FluentAssertions message.
        /// </summary>
        public async Task<bool> IsHomePageLoadedAsync()
        {
            try
            {
                await AppHeading.WaitForAsync();
                return await AppHeading.IsVisibleAsync();
            }
            catch (System.TimeoutException)
            {
                return false;
            }
        }

        public string CurrentUrl => _page.Url;

        /// <summary>
        /// Click on the Environments menu item in the sidebar
        /// Uses role-based locator (link with accessible name "Environments")
        /// </summary>
        public async Task ClickEnvironmentsMenu()
        {
            await EnvironmentsMenuLink.ClickAsync();
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }
}
