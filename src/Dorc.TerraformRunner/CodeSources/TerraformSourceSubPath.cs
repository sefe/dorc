using System.Linq;

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
            if (parts.Any(part => part.IndexOfAny(invalidChars) >= 0))
            {
                throw new ArgumentException(
                    $"Invalid sub-path '{subPath}'. Path contains invalid characters.",
                    nameof(subPath));
            }

            // string.Join over a vetted, non-rooted list of segments avoids the
            // Path.Combine(params) silent-discard rule. Each part has been
            // validated above to be a single non-rooted name.
            return string.Join(Path.DirectorySeparatorChar, parts);
        }

        public static async Task ApplyAsync(string workingDir, string subPath, CancellationToken cancellationToken)
        {
            // Reject parent-directory segments in the working dir up front;
            // Validate(subPath) handles the same for the user-supplied subpath.
            if (workingDir.Contains(".."))
            {
                throw new ArgumentException("workingDir must not contain parent-directory segments", nameof(workingDir));
            }

            var normalizedSubPath = Validate(subPath);

            // Path.Join concatenates without the silent-discard semantics of
            // Path.Combine; normalizedSubPath is guaranteed non-rooted by Validate.
            var subPathDir = Path.Join(workingDir, normalizedSubPath);
            if (!Directory.Exists(subPathDir))
            {
                throw new ArgumentException($"Terraform sub-path '{subPath}' not found in repository.");
            }

            // Stage as a sibling of workingDir, not under %TEMP%:
            // Directory.Move throws IOException across volumes, and on
            // hardened hosts %TEMP% and the terraform work root
            // (%ProgramData%\dorc\...) can live on different drives. A
            // sibling is on the same volume by construction.
            var workingDirParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDir)))
                ?? throw new ArgumentException("workingDir must have a parent directory", nameof(workingDir));
            var tempExtractDir = Path.Join(workingDirParent, $"terraform-extract-{Guid.NewGuid()}");
            // Created with the same restriction as the working directory because it is
            // moved over the top of it, taking its own DACL with it.
            RestrictedWorkingDirectory.Create(tempExtractDir);

            try
            {
                await DirectoryTreeCopy.CopyAsync(subPathDir, tempExtractDir, cancellationToken);
                ResilientDirectoryDeletion.Delete(workingDir);
                await MoveIntoPlaceAsync(tempExtractDir, workingDir, cancellationToken);
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

        // Directory.Delete returning does not guarantee the name is free on
        // Windows: anything holding a handle on the directory with
        // FILE_SHARE_DELETE (antivirus, search indexer) leaves the deletion
        // *pending*, and the name lingers until the handle closes. The swap
        // runs straight after a fresh git clone - exactly what scanners chew
        // on - so retry the move across the delete-pending window instead of
        // failing the whole deployment on the first collision.
        private static async Task MoveIntoPlaceAsync(
            string sourceDir,
            string destinationDir,
            CancellationToken cancellationToken)
        {
            const int maxAttempts = 6;
            const int baseDelayMs = 250;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Move(sourceDir, destinationDir);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(baseDelayMs * attempt, cancellationToken);
                    if (Directory.Exists(destinationDir))
                    {
                        try
                        {
                            ResilientDirectoryDeletion.Delete(destinationDir);
                        }
                        catch (IOException)
                        {
                            // Still delete-pending or locked; the next move
                            // attempt (after a longer delay) may succeed.
                        }
                    }
                }
            }
        }
    }
}
