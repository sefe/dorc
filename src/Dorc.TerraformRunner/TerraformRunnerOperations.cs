namespace Dorc.TerraformRunner
{
    public enum TerraformRunnerOperations
    {
        None = 0,
        CreatePlan = 1,
        ApplyPlan = 2,
        CreateDestroyPlan = 3,
        ApplyDestroyPlan = 4
    }
}
