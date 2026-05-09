namespace Dorc.TerraformRunner.CodeSources
{
    public static class DirectoryTreeCopy
    {
        public static async Task CopyAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            // Canonicalize at the entry point so any '..' segments in the
            // caller's input are resolved before they reach Directory.* APIs.
            // Defence-in-depth: callers are already trusted, but this prevents
            // a future, less-careful caller from accidentally introducing a
            // path-traversal source.
            var canonicalSource = Path.GetFullPath(sourceDir);
            var canonicalDest = Path.GetFullPath(destDir);
            await CopyCanonicalisedAsync(canonicalSource, canonicalDest, cancellationToken);
        }

        private static async Task CopyCanonicalisedAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dirName = Path.GetFileName(directory);
                var destSubDir = Path.Combine(destDir, dirName);
                await CopyCanonicalisedAsync(directory, destSubDir, cancellationToken);
            }
        }
    }
}
