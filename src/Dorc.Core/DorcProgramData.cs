namespace Dorc.Core
{
    /// <summary>System-wide root for DOrc local data — %ProgramData%\dorc.</summary>
    public static class DorcProgramData
    {
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "dorc");
    }
}
