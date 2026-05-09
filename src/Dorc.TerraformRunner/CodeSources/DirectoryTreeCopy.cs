namespace Dorc.TerraformRunner.CodeSources
{
    public static class DirectoryTreeCopy
    {
        public static async Task CopyAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
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
                await CopyAsync(directory, destSubDir, cancellationToken);
            }
        }
    }
}
