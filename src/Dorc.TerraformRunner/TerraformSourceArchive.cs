using System.IO.Compression;
using System.Text;

namespace Dorc.TerraformRunner
{
    // Builds the deployment source archive: the exact terraform configuration
    // an apply ran with, zipped for audit/rebuild purposes. The dispatcher
    // uploads the result to the same blob container as the plan artefacts.
    //
    // What is EXCLUDED, and why:
    //   - .git/        the full repository history; the provenance record pins
    //                  the resolved commit SHA instead.
    //   - .terraform/  provider binaries and module caches, hundreds of MB of
    //                  reproducible downloads; .terraform.lock.hcl (kept) pins
    //                  the versions needed to re-resolve them.
    //   - *.tfstate*   state never belongs in the archive; it lives in the
    //                  remote backend.
    // tfvars files (every resolved deployment property in cleartext) are kept
    // but passed through the caller's redaction function first, matching the
    // posture that led to the working directory being deleted even on failure.
    internal static class TerraformSourceArchive
    {
        public const string ProvenanceEntryName = "_dorc_provenance.json";

        private static readonly string[] ExcludedDirectoryNames = { ".git", ".terraform" };

        public static int Create(
            string workingDirectory,
            string archiveFilePath,
            string provenanceJson,
            Func<string, string> redact)
        {
            var root = Path.GetFullPath(workingDirectory);

            using var stream = new FileStream(archiveFilePath, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            var provenanceEntry = archive.CreateEntry(ProvenanceEntryName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(provenanceEntry.Open(), Encoding.UTF8))
            {
                writer.Write(provenanceJson);
            }

            var entryCount = 1;
            foreach (var file in EnumerateArchivableFiles(root))
            {
                var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                var fileName = Path.GetFileName(file);

                if (IsTfvarsFile(fileName))
                {
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write(redact(File.ReadAllText(file)));
                }
                else
                {
                    archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
                }

                entryCount++;
            }

            return entryCount;
        }

        private static IEnumerable<string> EnumerateArchivableFiles(string root)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(root, file);
                var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (segments.Any(s => ExcludedDirectoryNames.Contains(s, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var fileName = segments[^1];
                if (fileName.Contains(".tfstate", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }

        private static bool IsTfvarsFile(string fileName) =>
            fileName.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tfvars.json", StringComparison.OrdinalIgnoreCase);
    }
}
