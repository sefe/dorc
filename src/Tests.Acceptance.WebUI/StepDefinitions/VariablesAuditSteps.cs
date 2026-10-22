using FluentAssertions;
using FluentAssertions.Execution;
using Reqnroll;
using Tests.Acceptance.WebUI.Pages;
using Tests.Acceptance.WebUI.Pages.Components;
using Tests.Acceptance.WebUI.Support;

namespace Tests.Acceptance.WebUI.StepDefinitions
{
    /// <summary>
    /// Steps for Audit -> Variables Audit.
    /// </summary>
    [Binding]
    public class VariablesAuditSteps
    {
        private const string VariablesAuditMenu = "Variables Audit";

        // The audit entry must have been written by this run, not be an older change with the same values.
        private static readonly TimeSpan RecentEntryTolerance = TimeSpan.FromMinutes(10);

        private readonly SideNavigation _sideNavigation;
        private readonly VariablesAuditPage _auditPage;
        private readonly VariableTestContext _variable;

        public VariablesAuditSteps(SideNavigation sideNavigation, VariablesAuditPage auditPage, VariableTestContext variable)
        {
            _sideNavigation = sideNavigation;
            _auditPage = auditPage;
            _variable = variable;
        }

        [Then("the latest Variables Audit entry for \"(.*)\" on \"(.*)\" shows the original value changed to \"(.*)\"")]
        public async Task ThenLatestAuditShowsOriginalChangedTo(string variableName, string environmentName, string newValue)
        {
            await AssertLatestAuditEntryAsync(variableName, environmentName, _variable.OriginalValue!, newValue);
        }

        [Then("the latest Variables Audit entry for \"(.*)\" on \"(.*)\" shows \"(.*)\" changed back to the original value")]
        public async Task ThenLatestAuditShowsChangedBackToOriginal(string variableName, string environmentName, string oldValue)
        {
            await AssertLatestAuditEntryAsync(variableName, environmentName, oldValue, _variable.OriginalValue!);
        }

        private async Task AssertLatestAuditEntryAsync(string variableName, string environmentName, string expectedOld, string expectedNew)
        {
            await _sideNavigation.OpenAuditPageAsync(VariablesAuditMenu);
            _auditPage.CurrentUrl.Should().EndWith("/variables/audit",
                "Audit -> {0} should open the Variables Audit page", VariablesAuditMenu);

            await _auditPage.FilterAsync(variableName, environmentName);
            var entry = await _auditPage.GetLatestEntryAsync(variableName, environmentName);

            entry.Should().NotBeNull(
                "an audit entry for '{0}' on '{1}' should be listed after the change", variableName, environmentName);
            Console.WriteLine($"[Audit] Latest entry: {entry}");

            using (new AssertionScope($"latest Variables Audit entry {entry}"))
            {
                entry!.ChangeType.Should().Be("Update", "the value was edited, not added or deleted");
                entry.OldValue.Should().Be(expectedOld, "the audit should record the value before the change");
                entry.NewValue.Should().Be(expectedNew, "the audit should record the value after the change");
                entry.UpdatedAt.Should().NotBeNull("the Updated column should hold a date/time");
                entry.UpdatedAt!.Value.Should().BeCloseTo(DateTime.Now, RecentEntryTolerance,
                    "the top entry should be the change made by this test run");
            }
        }
    }
}
