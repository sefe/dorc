using System.IO.Compression;
using System.Text;
using Dorc.TerraformRunner.CodeSources;

namespace Dorc.TerraformRunner
{
    // Captures the plan's source once. Apply restores this snapshot and the
    // dispatcher publishes the same bytes for audit/rebuild purposes.
    //
    // What is EXCLUDED, and why:
    //   - .git/        the full repository history; the provenance record pins
    //                  the resolved commit SHA instead.
    //   - .terraform/  except modules: provider binaries are re-resolved using
    //                  the lock file; downloaded modules must not be re-fetched
    //                  from mutable refs between plan and apply.
    //   - *.tfstate*   state never belongs in the archive; it lives in the
    //                  remote backend.
    // tfvars files (every resolved deployment property in cleartext) are kept
    // but passed through the caller's redaction function first, matching the
    // posture that led to the working directory being deleted even on failure.
    internal static class TerraformSourceArchive
    {
        public const string ProvenanceEntryName = "_dorc_provenance.json";

        public static string PlanArchivePath(string planFilePath) => planFilePath + ".source.zip";

        public static void RestorePlanSource(string planFilePath, string workingDirectory)
        {
            var archivePath = PlanArchivePath(planFilePath);
            if (!File.Exists(archivePath))
                throw new FileNotFoundException(
                    "The approved plan's source archive is missing. Create and approve a new plan; mutable source refs cannot be re-fetched for apply.",
                    archivePath);

            new ZipArchiveExtractor(new ZipArchiveExtractionOptions()).Extract(archivePath, workingDirectory);
            if (!File.Exists(Path.Join(workingDirectory, ProvenanceEntryName)))
                throw new InvalidDataException("The approved plan's source archive has no provenance. Create and approve a new plan.");
        }

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
                if (relativePath.Equals(ProvenanceEntryName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Source must not contain reserved file '{ProvenanceEntryName}'.");

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

                if (segments.Any(s => s.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    || (segments.Any(s => s.Equals(".terraform", StringComparison.OrdinalIgnoreCase))
                        && !(segments.Length > 2
                            && segments[0].Equals(".terraform", StringComparison.OrdinalIgnoreCase)
                            && segments[1].Equals("modules", StringComparison.OrdinalIgnoreCase))))
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
