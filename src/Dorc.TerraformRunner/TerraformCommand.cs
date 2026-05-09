namespace Dorc.TerraformRunner
{
    // The terraform CLI subcommands DOrc invokes. Used by
    // TerraformProcessor.RunTerraformCommandAsync to drive the per-command
    // exit-code semantics defined in HLPS SC-03.
    internal enum TerraformCommand
    {
        Init,
        PlanDetailedExitCode,
        Apply,
        Show,
    }
}
