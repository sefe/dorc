using System.Text.Json;
using Microsoft.Playwright;

namespace Tests.Acceptance.WebUI.Pages.Components
{
    /// <summary>
    /// Shared helper for reading a Vaadin grid (vaadin-grid) reliably.
    ///
    /// WHY THIS EXISTS:
    /// Vaadin grids are virtualised and recycle their cells. The &lt;tr&gt; rows live in the grid's
    /// shadow DOM, while the visible cell content (vaadin-grid-cell-content) lives in light DOM in
    /// an order unrelated to row order. So "first cell in the DOM" is NOT "first row on screen".
    /// This helper maps shadow rows (tr.index = data row index) to their light-DOM cells via the
    /// &lt;slot&gt; names, giving a stable "row N, column M" view and a precise locator per cell.
    /// </summary>
    public class VaadinGrid
    {
        private const int PollIntervalMs = 500;

        private readonly ILocator _grid;

        // Reads all rendered, non-hidden rows ordered by data index (returned as JSON text, then
        // deserialised with System.Text.Json - Playwright's own converter can't map nested objects).
        private const string ReadRowsScript = @"grid => JSON.stringify([...grid.shadowRoot.querySelectorAll('tbody#items tr')]
            .filter(r => !r.hidden && typeof r.index === 'number')
            .sort((a, b) => a.index - b.index)
            .map(r => ({
                Index: r.index,
                Cells: [...r.querySelectorAll('td')].map(td => {
                    const slot = td.querySelector('slot');
                    const content = slot ? slot.assignedElements()[0] : null;
                    return {
                        Slot: slot ? slot.name : '',
                        Text: content ? content.innerText.replace(/\u00a0/g, ' ').trim() : '',
                        Part: td.getAttribute('part') || ''
                    };
                })
            })))";

        public VaadinGrid(ILocator grid)
        {
            _grid = grid;
        }

        /// <summary>
        /// Locator for the light-DOM content of one cell (use it to reach inputs/buttons inside the cell).
        /// </summary>
        public ILocator CellContent(GridCell cell) =>
            _grid.Locator($"vaadin-grid-cell-content[slot='{cell.Slot}']");

        public async Task<IReadOnlyList<GridRow>> GetRenderedRowsAsync()
        {
            var json = await _grid.EvaluateAsync<string>(ReadRowsScript);
            return JsonSerializer.Deserialize<List<GridRow>>(json) ?? new List<GridRow>();
        }

        /// <summary>
        /// Polls the grid until a rendered row matches the predicate (grids load data asynchronously
        /// from the server). Returns null if no row matches within the timeout.
        /// </summary>
        public async Task<GridRow?> WaitForRowAsync(Func<GridRow, bool> predicate, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                var match = (await GetRenderedRowsAsync()).FirstOrDefault(predicate);
                if (match != null)
                    return match;

                await Task.Delay(PollIntervalMs);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }
    }

    public class GridRow
    {
        public int Index { get; set; }
        public List<GridCell> Cells { get; set; } = new();

        public string CellText(int columnIndex) =>
            columnIndex < Cells.Count ? Cells[columnIndex].Text : string.Empty;
    }

    public class GridCell
    {
        public string Slot { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;

        /// <summary>The td "part" attribute - DOrc uses it to tag rows, e.g. "insert-type" / "delete-type".</summary>
        public string Part { get; set; } = string.Empty;
    }
}
