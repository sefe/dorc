namespace Dorc.Core
{
    /// <summary>System-wide root for DOrc local data — %ProgramData%\dorc.</summary>
    public static class DorcProgramData
    {
        public static string Root => Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "dorc");
    }
}
