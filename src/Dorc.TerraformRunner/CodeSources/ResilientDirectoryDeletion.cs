namespace Dorc.TerraformRunner.CodeSources
{
    public static class ResilientDirectoryDeletion
    {
        private const int MaxRetries = 3;
        private const int InitialDelayMs = 100;

        public static void Delete(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    StripReadOnly(directory);
                    Directory.Delete(directory, true);
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
                StripReadOnly(directory);
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to delete directory '{directory}' after {MaxRetries} attempts.", ex);
            }
        }

        private static void StripReadOnly(string directory)
        {
            var directoryInfo = new DirectoryInfo(directory);

            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
            }

            foreach (var file in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            foreach (var dir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    dir.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
        }
    }
}
