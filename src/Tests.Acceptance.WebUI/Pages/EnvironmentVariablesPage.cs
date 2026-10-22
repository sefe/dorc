using Microsoft.Playwright;
using Tests.Acceptance.WebUI.Pages.Components;
using Tests.Acceptance.WebUI.Support;

namespace Tests.Acceptance.WebUI.Pages
{
    /// <summary>
    /// Environment details -> "Variables" tab (route /environment/{name}/variables, component &lt;env-variables&gt;).
    ///
    /// Grid columns: 0 Variable Name | 1 Variable Scope | 2 Secure | 3 Variable Value (variable-value-controls:
    /// text field + Edit / Save / Cancel / Delete Value buttons).
    /// All locators are scoped to &lt;env-variables&gt; so the side bar / other tabs can never match.
    /// </summary>
    public class EnvironmentVariablesPage
    {
        private const int DataLoadTimeoutMs = 60000;
        private const int NameColumn = 0;
        private const int PollIntervalMs = 500;

        private readonly IPage _page;
        private readonly string _baseUrl;
        private readonly VaadinGrid _grid;

        private ILocator Root => _page.Locator("env-variables");
        private ILocator NameFilter => Root.Locator("vaadin-text-field[placeholder='Name'] input");
        private ILocator VariablesGrid => Root.Locator("vaadin-grid#grid");

        // Inside a value cell (variable-value-controls)
        private static ILocator ValueControls(ILocator cell) => cell.Locator("variable-value-controls");
        private static ILocator ValueInput(ILocator cell) => ValueControls(cell).Locator("vaadin-text-field input");
        private static ILocator EditButton(ILocator cell) =>
            ValueControls(cell).GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true });
        private static ILocator SaveButton(ILocator cell) =>
            ValueControls(cell).GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

        public EnvironmentVariablesPage(IPage page)
        {
            _page = page;
            _baseUrl = TestConfiguration.GetBaseUrl().TrimEnd('/');
            _grid = new VaadinGrid(VariablesGrid);
        }

        public string CurrentUrl => _page.Url;

        /// <summary>
        /// Opens the environment's Variables tab directly by URL (DOrc deep link) and waits for the grid.
        /// </summary>
        public async Task OpenAsync(string environmentName)
        {
            await _page.GotoAsync($"{_baseUrl}/environment/{Uri.EscapeDataString(environmentName)}/variables");
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await NameFilter.WaitForAsync(new() { Timeout = DataLoadTimeoutMs });
        }

        /// <summary>
        /// Narrows the grid with the "Name" column filter (server-side, contains-match).
        /// </summary>
        public async Task FilterByVariableNameAsync(string variableName)
        {
            await NameFilter.FillAsync(variableName);
        }

        /// <summary>
        /// Current value shown for the variable, or null when the variable has no row on this environment.
        /// </summary>
        public async Task<string?> GetVariableValueAsync(string variableName)
        {
            var cell = await FindValueCellAsync(variableName);
            return cell == null ? null : await ValueInput(cell).InputValueAsync();
        }

        /// <summary>
        /// True when the Edit button for the variable's value is enabled (value is UserEditable for this user).
        /// </summary>
        public async Task<bool> CanEditVariableAsync(string variableName)
        {
            var cell = await FindValueCellAsync(variableName);
            return cell != null && await EditButton(cell).IsEnabledAsync();
        }

        /// <summary>
        /// Edit -> type new value -> Save, on the variable's row.
        /// </summary>
        public async Task UpdateVariableValueAsync(string variableName, string newValue)
        {
            var cell = await FindValueCellAsync(variableName)
                ?? throw new InvalidOperationException(
                    $"Variable '{variableName}' is not listed on the Variables tab ({CurrentUrl}).");

            await EditButton(cell).ClickAsync();
            await SaveButton(cell).WaitForAsync();
            await ValueInput(cell).FillAsync(newValue);
            await SaveButton(cell).ClickAsync();
        }

        /// <summary>
        /// Polls until the variable shows the expected value (the grid refreshes after Save).
        /// Returns the last value seen, so the caller can assert with a readable message.
        /// </summary>
        public async Task<string?> WaitForVariableValueAsync(string variableName, string expectedValue)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(TestConfiguration.GetTimeout());
            string? actual;
            do
            {
                actual = await GetVariableValueAsync(variableName);
                if (actual == expectedValue)
                    return actual;

                await Task.Delay(PollIntervalMs);
            }
            while (DateTime.UtcNow < deadline);

            return actual;
        }

        /// <summary>
        /// Finds the row whose Variable Name is an exact match (the filter is contains-match) and
        /// returns the cell that hosts the value controls.
        /// </summary>
        private async Task<ILocator?> FindValueCellAsync(string variableName)
        {
            var row = await _grid.WaitForRowAsync(
                r => string.Equals(r.CellText(NameColumn), variableName, StringComparison.Ordinal),
                DataLoadTimeoutMs);
            if (row == null)
                return null;

            foreach (var cell in row.Cells)
            {
                var content = _grid.CellContent(cell);
                if (await ValueControls(content).CountAsync() > 0)
                    return content;
            }

            return null;
        }
    }
}
