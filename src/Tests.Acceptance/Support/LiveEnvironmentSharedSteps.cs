namespace Tests.Acceptance.Support
{
    [Binding]
    public class LiveEnvironmentSharedSteps
    {
        [Given("the live DOrc environment is enabled and the synthetic regression baseline is provisioned")]
        public void GivenTheLiveDorcEnvironmentIsEnabledAndTheSyntheticRegressionBaselineIsProvisioned()
        {
            AcceptanceProjectProvisioner.AssertLiveEnvironmentEnabled();
        }
    }
}
