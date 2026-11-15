namespace Dorc.TerraformRunner.CodeSources
{
    public static class DirectoryTreeCopy
    {
        public static async Task CopyAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            // Reject parent-directory segments in raw inputs before they
            // reach Directory.*/Path.* APIs. Redundant with the
            // canonicalisation below, but keeps the contract explicit and
            // satisfies SAST heuristics that don't model GetFullPath as
            // sanitisation.
            if (sourceDir.Contains("..") || destDir.Contains(".."))
            {
                throw new ArgumentException("paths must not contain parent-directory segments");
            }

            // Canonicalize at the entry point so any '..' segments in the
            // caller's input are resolved before they reach Directory.* APIs.
            // Defence-in-depth: callers are already trusted, but this prevents
            // a future, less-careful caller from accidentally introducing a
            // path-traversal source.
            var canonicalSource = Path.GetFullPath(sourceDir);
            var canonicalDest = Path.GetFullPath(destDir);
            RejectIfReparsePoint(canonicalSource);
            await CopyCanonicalisedAsync(canonicalSource, canonicalDest, cancellationToken);
        }

        private static void RejectIfReparsePoint(string entryPath)
        {
            var attributes = File.GetAttributes(entryPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to copy reparse point '{entryPath}' from artifact/source directory.");
            }
        }

        private static async Task CopyCanonicalisedAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            RejectIfReparsePoint(sourceDir);

            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectIfReparsePoint(file);
                var fileName = Path.GetFileName(file);
                if (Path.IsPathRooted(fileName))
                {
                    throw new InvalidOperationException(
                        $"GetFileName returned a rooted path: '{fileName}'. Refusing to combine.");
                }
                var destFile = Path.Join(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectIfReparsePoint(directory);
                var dirName = Path.GetFileName(directory);
                if (Path.IsPathRooted(dirName))
                {
                    throw new InvalidOperationException(
                        $"GetFileName returned a rooted path: '{dirName}'. Refusing to combine.");
                }
                var destSubDir = Path.Join(destDir, dirName);
                await CopyCanonicalisedAsync(directory, destSubDir, cancellationToken);
            }
        }
    }
}
