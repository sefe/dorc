namespace Dorc.TerraformRunner.CodeSources
{
    public static class DirectoryHelper
    {
        public static async Task ExtractSubPathAsync(string workingDir, string subPath, CancellationToken cancellationToken)
        {
            var subPathDir = Path.Combine(workingDir, subPath);
            if (Directory.Exists(subPathDir))
            {
                var tempDir = Path.Combine(Path.GetTempPath(), $"terraform-temp-{Guid.NewGuid()}");
                Directory.Move(workingDir, tempDir);
                Directory.CreateDirectory(workingDir);

                var subPathInTemp = Path.Combine(tempDir, subPath);
                await CopyDirectoryAsync(subPathInTemp, workingDir, cancellationToken);

                // Clean up temp directory
                SafeRemoveDirectory(tempDir);
            }
            else
            {
                throw new ArgumentException($"Terraform sub-path '{subPath}' not found in repository.");
            }
        }

        public static void SafeRemoveDirectory(string tempDir)
        {
            try
            {
                RemoveReadOnlyAttributes(tempDir);
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed to delete directory '{tempDir}'", ex);
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
    }
}
