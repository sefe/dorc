using System.Linq;

namespace Dorc.TerraformRunner.CodeSources
{
    public static class ResilientDirectoryDeletion
    {
        private const int MaxRetries = 3;
        private const int InitialDelayMs = 100;

        public static void Delete(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;

            // Canonicalize at the entry point. Any '..' segments are resolved
            // before they reach Directory.Delete; defence-in-depth in line with
            // the path-traversal contract DOrc enforces elsewhere.
            var canonical = Path.GetFullPath(directory);
            if (!Directory.Exists(canonical)) return;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    StripReadOnly(canonical);
                    Directory.Delete(canonical, true);
                    return;
                }
                catch (UnauthorizedAccessException) when (attempt < MaxRetries)
                {
                    Thread.Sleep(InitialDelayMs * attempt);
                }
                catch (IOException) when (attempt < MaxRetries)
                {
                    Thread.Sleep(InitialDelayMs * attempt);
                }
            }

            try
            {
                StripReadOnly(canonical);
                Directory.Delete(canonical, true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Failed to delete directory '{canonical}' after {MaxRetries} attempts.", ex);
            }
            catch (IOException ex)
            {
                throw new IOException($"Failed to delete directory '{canonical}' after {MaxRetries} attempts.", ex);
            }
        }

        private static void StripReadOnly(string directory)
        {
            var directoryInfo = new DirectoryInfo(directory);

            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
            }

            foreach (var file in directoryInfo.GetFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Attributes.HasFlag(FileAttributes.ReadOnly)))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }

            foreach (var dir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories)
                .Where(d => d.Attributes.HasFlag(FileAttributes.ReadOnly)))
            {
                dir.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }
}
