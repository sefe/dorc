namespace Dorc.TerraformRunner.CodeSources
{
    public static class TerraformSourceSubPath
    {
        public static string Validate(string subPath)
        {
            if (string.IsNullOrWhiteSpace(subPath))
            {
                throw new ArgumentException("Sub-path cannot be null or empty.", nameof(subPath));
            }

            subPath = subPath.Trim('/', '\\', ' ');

            if (subPath.Contains(".."))
            {
                throw new ArgumentException(
                    $"Invalid sub-path '{subPath}'. Parent directory references (..) are not allowed.",
                    nameof(subPath));
            }

            if (Path.IsPathRooted(subPath))
            {
                throw new ArgumentException(
                    $"Invalid sub-path '{subPath}'. Absolute paths are not allowed.",
                    nameof(subPath));
            }

            var parts = subPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                throw new ArgumentException("Sub-path cannot be empty after normalization.", nameof(subPath));
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var part in parts)
            {
                if (part.IndexOfAny(invalidChars) >= 0)
                {
                    throw new ArgumentException(
                        $"Invalid sub-path '{subPath}'. Path contains invalid characters.",
                        nameof(subPath));
                }
            }

            return Path.Combine(parts);
        }

        public static async Task ApplyAsync(string workingDir, string subPath, CancellationToken cancellationToken)
        {
            var normalizedSubPath = Validate(subPath);

            var subPathDir = Path.Combine(workingDir, normalizedSubPath);
            if (!Directory.Exists(subPathDir))
            {
                throw new ArgumentException($"Terraform sub-path '{subPath}' not found in repository.");
            }

            var tempExtractDir = Path.Combine(Path.GetTempPath(), $"terraform-extract-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                await DirectoryTreeCopy.CopyAsync(subPathDir, tempExtractDir, cancellationToken);
                ResilientDirectoryDeletion.Delete(workingDir);
                Directory.Move(tempExtractDir, workingDir);
            }
            catch
            {
                if (Directory.Exists(tempExtractDir))
                {
                    ResilientDirectoryDeletion.Delete(tempExtractDir);
                }
                throw;
            }
        }
    }
}
