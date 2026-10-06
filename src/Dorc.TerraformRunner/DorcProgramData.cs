namespace Dorc.TerraformRunner
{
    /// <summary>System-wide root for DOrc local data — %ProgramData%\dorc.</summary>
    internal static class DorcProgramData
    {
        public static string Root => Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "dorc");
    }
}
