using FluentAssertions;
using Reqnroll;
using Tests.Acceptance.WebUI.Pages;
using Tests.Acceptance.WebUI.Pages.Components;
using Tests.Acceptance.WebUI.Support;

namespace Tests.Acceptance.WebUI.StepDefinitions
{
    /// <summary>
    /// Steps for the Environment details -> Variables tab.
    /// Shared scenario state (original value, revert flag) lives in VariableTestContext (context injection).
    /// </summary>
    [Binding]
    public class EnvironmentVariablesSteps
    {
        private const string SavedNotification = "Variable value saved successfully!";

        private readonly EnvironmentVariablesPage _variablesTab;
        private readonly AppNotification _notification;
        private readonly VariableTestContext _variable;

        public EnvironmentVariablesSteps(
            EnvironmentVariablesPage variablesTab,
            AppNotification notification,
            VariableTestContext variable)
        {
            _variablesTab = variablesTab;
            _notification = notification;
            _variable = variable;
        }

        [Given("the variable \"(.*)\" has an editable value on the Variables tab of environment \"(.*)\"")]
        public async Task GivenTheVariableHasAnEditableValue(string variableName, string environmentName)
        {
            _variable.VariableName = variableName;
            _variable.EnvironmentName = environmentName;

            await OpenFilteredVariablesTabAsync();
            _variablesTab.CurrentUrl.Should().EndWith("/variables",
                "the Variables tab of environment '{0}' should be open", environmentName);

            var originalValue = await _variablesTab.GetVariableValueAsync(variableName);
            originalValue.Should().NotBeNull(
                "variable '{0}' must already have a value on environment '{1}' (test pre-condition)",
                variableName, environmentName);

            var canEdit = await _variablesTab.CanEditVariableAsync(variableName);
            canEdit.Should().BeTrue(
                "the test user needs edit rights on '{0}' to change variable '{1}' (test pre-condition)",
                environmentName, variableName);

            _variable.OriginalValue = originalValue;
            Console.WriteLine($"[Variable] Original value of '{variableName}' on '{environmentName}': '{originalValue}'");
        }

        [When("the user updates the value of variable \"(.*)\" to \"(.*)\"")]
        public async Task WhenTheUserUpdatesTheValue(string variableName, string newValue)
        {
            newValue.Should().NotBe(_variable.OriginalValue,
                "the new value must differ from the current value '{0}', otherwise no change (and no audit entry) is made",
                _variable.OriginalValue);

            // Flag BEFORE saving: from here on, any failure must trigger the revert hook.
            _variable.RevertPending = true;
            await _variablesTab.UpdateVariableValueAsync(variableName, newValue);
        }

        [When("the user reverts the variable \"(.*)\" to its original value")]
        public async Task WhenTheUserRevertsTheVariable(string variableName)
        {
            await OpenFilteredVariablesTabAsync();
            await _variablesTab.UpdateVariableValueAsync(variableName, _variable.OriginalValue!);
        }

        [Then("the variable \"(.*)\" shows the value \"(.*)\" on the Variables tab")]
        public async Task ThenTheVariableShowsTheValue(string variableName, string expectedValue)
        {
            await AssertValueSavedAsync(variableName, expectedValue);
        }

        [Then("the variable \"(.*)\" shows its original value on the Variables tab")]
        public async Task ThenTheVariableShowsItsOriginalValue(string variableName)
        {
            await AssertValueSavedAsync(variableName, _variable.OriginalValue!);
            _variable.RevertPending = false;
        }

        private async Task OpenFilteredVariablesTabAsync()
        {
            await _variablesTab.OpenAsync(_variable.EnvironmentName);
            await _variablesTab.FilterByVariableNameAsync(_variable.VariableName);
        }

        private async Task AssertValueSavedAsync(string variableName, string expectedValue)
        {
            var saved = await _notification.IsShownAsync(SavedNotification);
            saved.Should().BeTrue("DOrc should confirm the save with '{0}'", SavedNotification);

            var actual = await _variablesTab.WaitForVariableValueAsync(variableName, expectedValue);
            actual.Should().Be(expectedValue,
                "variable '{0}' on '{1}' should show the saved value", variableName, _variable.EnvironmentName);
        }
    }
}
