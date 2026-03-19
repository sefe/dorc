namespace Dorc.TerraformRunner.CodeSources
{
    public static class DirectoryHelper
    {
        public static async Task ExtractSubPathAsync(string workingDir, string subPath, CancellationToken cancellationToken)
        {
            var normalizedSubPath = ValidateAndNormalizeSubPath(subPath);

            var subPathDir = Path.Combine(workingDir, normalizedSubPath);
            if (!Directory.Exists(subPathDir))
            {
                throw new ArgumentException($"Terraform sub-path '{subPath}' not found in repository.");
            }

            // Create a new temp directory to hold the extracted subpath contents
            var tempExtractDir = Path.Combine(Path.GetTempPath(), $"terraform-extract-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                // Copy the subpath contents to the temp directory
                await CopyDirectoryAsync(subPathDir, tempExtractDir, cancellationToken);

                // Delete the original working directory
                SafeRemoveDirectory(workingDir);

                // Move the temp directory to replace the working directory
                Directory.Move(tempExtractDir, workingDir);
            }
            catch
            {
                // Clean up temp directory if something went wrong
                if (Directory.Exists(tempExtractDir))
                {
                    SafeRemoveDirectory(tempExtractDir);
                }
                throw;
            }
        }

        public static void SafeRemoveDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            const int maxRetries = 3;
            const int delayMs = 100;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    RemoveReadOnlyAttributes(directory);
                    Directory.Delete(directory, true);
                    return; // Success
                }
                catch (UnauthorizedAccessException) when (attempt < maxRetries)
                {
                    // Wait and retry - files might be temporarily locked
                    Thread.Sleep(delayMs * attempt);
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    // Wait and retry - directory might be in use
                    Thread.Sleep(delayMs * attempt);
                }
            }

            // Last attempt without catching
            try
            {
                RemoveReadOnlyAttributes(directory);
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to delete directory '{directory}' after {maxRetries} attempts.", ex);
            }
        }

        public static async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            // Create destination directory if it doesn't exist
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Copy all files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            // Copy all subdirectories recursively
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(directory);
                var destSubDir = Path.Combine(destDir, dirName);
                await CopyDirectoryAsync(directory, destSubDir, cancellationToken);
            }
        }

        public static void RemoveReadOnlyAttributes(string directory)
        {
            var directoryInfo = new DirectoryInfo(directory);

            // Remove readonly from the directory itself
            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
            }

            // Remove readonly from all files
            foreach (var file in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            // Remove readonly from all subdirectories
            foreach (var dir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    dir.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
        }

        public static string ValidateAndNormalizeSubPath(string subPath)
        {
            if (string.IsNullOrWhiteSpace(subPath))
            {
                throw new ArgumentException("Sub-path cannot be null or empty.", nameof(subPath));
            }

            // Trim leading/trailing slashes and backslashes
            subPath = subPath.Trim('/', '\\', ' ');

            // Reject paths that contain directory traversal sequences
            if (subPath.Contains(".."))
            {
                throw new ArgumentException(
                    $"Invalid sub-path '{subPath}'. Parent directory references (..) are not allowed.",
                    nameof(subPath));
            }

            // Reject absolute paths (drive letters on Windows or root on Unix)
            if (Path.IsPathRooted(subPath))
            {
                throw new ArgumentException(
                    $"Invalid sub-path '{subPath}'. Absolute paths are not allowed.",
                    nameof(subPath));
            }

            // Split on both types of separators and remove empty entries
            var parts = subPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                throw new ArgumentException("Sub-path cannot be empty after normalization.", nameof(subPath));
            }

            // Validate each path component doesn't contain invalid characters
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

            // Recombine using Path.Combine for OS-correct separators
            return Path.Combine(parts);
        }
    }
}
