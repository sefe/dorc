using Microsoft.Playwright;

namespace Tests.Acceptance.WebUI.Pages
{
    /// <summary>
    /// EnvironmentsPage - Encapsulates all Environments section interactions.
    /// Follows Coral.Testing Page Object pattern for maintainability and reusability.
    /// 
    /// LOCATOR STRATEGY:
    /// This project uses role-based locators (getByRole, getByLabel) instead of ID selectors.
    /// WHY: DOrc UI is built with Lit 3 + Vaadin Web Components, which use Shadow DOM.
    /// Shadow DOM requires locators that pierce across component boundaries.
    /// - getByRole() and getByLabel() reliably work across shadow DOM levels
    /// - id= selectors often fail because IDs do not pierce shadow DOM
    /// - This approach was validated in a local spike against real DOrc components
    /// </summary>
    public class EnvironmentsPage

    {
        private readonly IPage _page;

        // Locators: defined once here so selectors live in a single place.
        // Playwright locators are lazy — the DOM is queried only when an action runs.
        // Vaadin puts placeholder="Search" on both <vaadin-text-field> and its inner <input>; target the input.
        private ILocator SearchBox => _page.Locator("vaadin-text-field[placeholder='Search'] input");

        // Any rendered grid cell = environment data has arrived (the API call takes ~10s locally).
        private ILocator AnyGridCell => _page.GetByRole(AriaRole.Gridcell).First;

        // Parameterised locator: depends on runtime data, so it is a method rather than a property.
        // .First: the name can also appear in the Description/Notes cells of the same row.
        private ILocator ProjectCell(string projectName) =>
            _page.GetByRole(AriaRole.Gridcell, new() { Name = projectName, Exact = true }).First;

        public EnvironmentsPage(IPage page)
        {
            _page = page;
        }

        /// <summary>
        /// Search for a project by name in the search bar, once the grid has loaded its data
        /// </summary>
        public async Task SearchProject(string projectName)
        {
            await AnyGridCell.WaitForAsync(new() { Timeout = 60000 });
            await SearchBox.FillAsync(projectName);
        }

        /// <summary>
        /// Waits for the project's grid cell; returns false (instead of throwing) if it doesn't appear,
        /// so the step definition can assert with a readable FluentAssertions message.
        /// </summary>
        public async Task<bool> IsProjectDisplayedAsync(string projectName)
        {
            var projectCell = ProjectCell(projectName);
            try
            {
                await projectCell.WaitForAsync();
                return await projectCell.IsVisibleAsync();
            }
            catch (System.TimeoutException)
            {
                return false;
            }
        }
    }
}
