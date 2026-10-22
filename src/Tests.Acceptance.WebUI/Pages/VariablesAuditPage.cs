using System.Globalization;
using Microsoft.Playwright;
using Tests.Acceptance.WebUI.Pages.Components;

namespace Tests.Acceptance.WebUI.Pages
{
    /// <summary>
    /// Audit -> Variables Audit (route /variables/audit, component &lt;page-variables-audit&gt;).
    ///
    /// Grid is server-side, sorted by Updated (newest first). Columns:
    /// 0 Property Name | 1 Environment | 2 User | 3 Updated (dd/MM/yyyy HH:mm:ss) | 4 Value (div#old + div#new).
    /// Row type is exposed through the cell "part" attribute: insert-type / delete-type / (none) = update.
    /// </summary>
    public class VariablesAuditPage
    {
        private const int DataLoadTimeoutMs = 60000;
        private const int NameColumn = 0;
        private const int EnvironmentColumn = 1;
        private const int UserColumn = 2;
        private const int UpdatedColumn = 3;
        private const int ValueColumn = 4;

        private readonly IPage _page;
        private readonly VaadinGrid _grid;

        private ILocator Root => _page.Locator("page-variables-audit");
        private ILocator NameFilter => Root.Locator("vaadin-text-field[placeholder='Name'] input");
        private ILocator EnvironmentFilter => Root.Locator("vaadin-text-field[placeholder='Environment'] input");
        private ILocator AuditGrid => Root.Locator("vaadin-grid#grid");

        public VariablesAuditPage(IPage page)
        {
            _page = page;
            _grid = new VaadinGrid(AuditGrid);
        }

        public string CurrentUrl => _page.Url;

        public async Task FilterAsync(string variableName, string environmentName)
        {
            await NameFilter.FillAsync(variableName);
            await EnvironmentFilter.FillAsync(environmentName);
        }

        /// <summary>
        /// Most recent audit entry (top row) for the variable on the environment, or null if none appears.
        /// Waits until the top row reflects the filter, so an unfiltered first page is never read by mistake.
        /// </summary>
        public async Task<VariableAuditEntry?> GetLatestEntryAsync(string variableName, string environmentName)
        {
            var row = await _grid.WaitForRowAsync(
                r => r.Index == 0
                     && r.CellText(NameColumn) == variableName
                     && r.CellText(EnvironmentColumn) == environmentName,
                DataLoadTimeoutMs);
            if (row == null || row.Cells.Count <= ValueColumn)
                return null;

            var valueCell = _grid.CellContent(row.Cells[ValueColumn]);
            var part = row.Cells[NameColumn].Part;

            return new VariableAuditEntry
            {
                VariableName = row.CellText(NameColumn),
                EnvironmentName = row.CellText(EnvironmentColumn),
                UpdatedBy = row.CellText(UserColumn),
                UpdatedAt = ParseUpdated(row.CellText(UpdatedColumn)),
                OldValue = Normalise(await valueCell.Locator("#old").InnerTextAsync()),
                NewValue = Normalise(await valueCell.Locator("#new").InnerTextAsync()),
                ChangeType = part.Contains("insert-type") ? "Insert"
                           : part.Contains("delete-type") ? "Delete"
                           : "Update"
            };
        }

        private static string Normalise(string text) => text.Replace('\u00a0', ' ').Trim();

        // Rendered by the browser as en-GB local time, e.g. "04/07/2023 15:31:00".
        private static DateTime? ParseUpdated(string text) =>
            DateTime.TryParseExact(text, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var value) ? value : null;
    }

    public class VariableAuditEntry
    {
        public string VariableName { get; init; } = string.Empty;
        public string EnvironmentName { get; init; } = string.Empty;
        public string UpdatedBy { get; init; } = string.Empty;
        public DateTime? UpdatedAt { get; init; }
        public string OldValue { get; init; } = string.Empty;
        public string NewValue { get; init; } = string.Empty;
        public string ChangeType { get; init; } = string.Empty;

        public override string ToString() =>
            $"[{ChangeType}] {VariableName} @ {EnvironmentName} by {UpdatedBy} at {UpdatedAt:dd/MM/yyyy HH:mm:ss}: '{OldValue}' -> '{NewValue}'";
    }
}
