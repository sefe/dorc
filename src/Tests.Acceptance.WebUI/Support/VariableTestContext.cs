namespace Tests.Acceptance.WebUI.Support
{
    /// <summary>
    /// Scenario-scoped test data for variable scenarios.
    /// Reqnroll context injection creates ONE instance per scenario and shares it with every step class
    /// and hook that asks for it in its constructor - no ScenarioContext["key"] string look-ups.
    /// </summary>
    public class VariableTestContext
    {
        public string VariableName { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;

        /// <summary>Value captured before the scenario changes anything - the revert target.</summary>
        public string? OriginalValue { get; set; }

        /// <summary>
        /// True from the moment the value is changed until it has been verified back at OriginalValue.
        /// The revert hook uses it to restore the environment when the scenario fails part-way.
        /// </summary>
        public bool RevertPending { get; set; }
    }
}
