using Microsoft.Playwright;
using Reqnroll;
using Tests.Acceptance.WebUI.Pages;

namespace Tests.Acceptance.WebUI.Support
{
    /// <summary>
    /// Safety net for scenarios tagged @Revert:VariableValue.
    /// If the scenario changed a variable value and did not get as far as restoring it (any failure in between),
    /// this hook puts the original value back through the UI, so shared environments are never left modified.
    ///
    /// Runs BEFORE BrowserHooks.AfterScenario (lower Order) because it needs the browser that is about to close.
    /// </summary>
    [Binding]
    public class VariableValueRevertHook
    {
        private readonly VariableTestContext _variable;
        private readonly ScenarioContext _scenarioContext;
        private readonly IPage _page;

        public VariableValueRevertHook(VariableTestContext variable, ScenarioContext scenarioContext, IPage page)
        {
            _variable = variable;
            _scenarioContext = scenarioContext;
            _page = page;
        }

        [AfterScenario("Revert:VariableValue", Order = 100)]
        public async Task RevertVariableValueIfPending()
        {
            if (!_variable.RevertPending || _variable.OriginalValue == null)
                return;

            var target = $"'{_variable.VariableName}' on '{_variable.EnvironmentName}' back to '{_variable.OriginalValue}'";
            Console.WriteLine($"[Revert] Scenario did not restore the value - reverting {target}.");

            // Fresh tab: the scenario's tab may be mid-edit or showing a dialog after a failure.
            var page = await _page.Context.NewPageAsync();
            try
            {
                var variablesTab = new EnvironmentVariablesPage(page);
                await variablesTab.OpenAsync(_variable.EnvironmentName);
                await variablesTab.FilterByVariableNameAsync(_variable.VariableName);

                if (await variablesTab.GetVariableValueAsync(_variable.VariableName) != _variable.OriginalValue)
                    await variablesTab.UpdateVariableValueAsync(_variable.VariableName, _variable.OriginalValue);

                var actual = await variablesTab.WaitForVariableValueAsync(_variable.VariableName, _variable.OriginalValue);
                if (actual != _variable.OriginalValue)
                    throw new InvalidOperationException($"value is still '{actual}' after the revert attempt");

                _variable.RevertPending = false;
                Console.WriteLine($"[Revert] Done - {target}.");
            }
            catch (Exception ex)
            {
                var message = $"[Revert] FAILED to revert {target}: {ex.Message}. MANUAL ACTION REQUIRED in DOrc.";
                Console.WriteLine(message);

                // Surface it as a failure when the scenario itself passed; otherwise keep the original error visible.
                if (_scenarioContext.TestError == null)
                    throw new InvalidOperationException(message, ex);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
    }
}
