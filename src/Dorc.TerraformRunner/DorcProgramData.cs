namespace Dorc.TerraformRunner
{
    /// <summary>
    /// System-wide root for DOrc local data: %ProgramData%\dorc, typically
    /// C:\ProgramData\dorc. Used as a base for artifact downloads and
    /// transient working folders so paths are stable across service accounts
    /// instead of resolving under whichever user profile the calling process
    /// happens to run as.
    /// </summary>
    internal static class DorcProgramData
    {
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "dorc");
    }
}
