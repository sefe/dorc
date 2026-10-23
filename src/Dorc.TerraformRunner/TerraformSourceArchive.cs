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
    // Variable files are never retained: saved plans already contain their
    // execution inputs. Value-based redaction cannot safely sanitize arbitrary
    // inherited properties or source-provided variable files.
    internal static class TerraformSourceArchive
    {
        public const string ProvenanceEntryName = "_dorc_provenance.json";

        public static string PlanArchivePath(string planFilePath) => planFilePath + ".source.zip";

        public static void RestorePlanSource(string planFilePath, string workingDirectory, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archivePath = PlanArchivePath(planFilePath);
            if (!File.Exists(archivePath))
                throw new FileNotFoundException(
                    "The approved plan's source archive is missing. Create and approve a new plan; mutable source refs cannot be re-fetched for apply.",
                    archivePath);

            new ZipArchiveExtractor(new ZipArchiveExtractionOptions()).Extract(archivePath, workingDirectory, cancellationToken);
            if (!File.Exists(Path.Join(workingDirectory, ProvenanceEntryName)))
                throw new InvalidDataException("The approved plan's source archive has no provenance. Create and approve a new plan.");
        }

        public static int Create(
            string workingDirectory,
            string archiveFilePath,
            string provenanceJson,
            ZipArchiveExtractionOptions? limits = null,
            CancellationToken cancellationToken = default)
        {
            limits ??= ZipArchiveExtractionOptions.Default;
            var root = Path.GetFullPath(workingDirectory);

            using var stream = new FileStream(archiveFilePath, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            var entryCount = 0;
            long totalBytes = 0;
            void AddEntry(string name, Stream source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entryCount > limits.MaxEntryCount)
                    throw new UnsafeArchiveException(UnsafeArchiveReason.EntryCountExceeded, name,
                        $"Source archive exceeds {limits.MaxEntryCount} entries at '{name}'; reduce the source before creating a new plan.");
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var destination = entry.Open();
                var buffer = new byte[81920];
                long entryBytes = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entryBytes += read;
                    totalBytes += read;
                    if (entryBytes > limits.MaxBytesPerEntry)
                        throw new UnsafeArchiveException(UnsafeArchiveReason.EntrySizeExceeded, name,
                            $"Source file '{name}' exceeds {limits.MaxBytesPerEntry} bytes; reduce it before creating a new plan.");
                    if (totalBytes > limits.MaxBytesTotal)
                        throw new UnsafeArchiveException(UnsafeArchiveReason.TotalSizeExceeded, name,
                            $"Source archive exceeds {limits.MaxBytesTotal} uncompressed bytes at '{name}'; reduce the source before creating a new plan.");
                    destination.Write(buffer, 0, read);
                }
            }

            using (var provenance = new MemoryStream(Encoding.UTF8.GetBytes(provenanceJson)))
                AddEntry(ProvenanceEntryName, provenance);
            foreach (var file in EnumerateArchivableFiles(root))
            {
                var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relativePath.Equals(ProvenanceEntryName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Source must not contain reserved file '{ProvenanceEntryName}'.");

                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                AddEntry(relativePath, source);
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
                if (fileName.Contains(".tfstate", StringComparison.OrdinalIgnoreCase)
                    || fileName.Contains(".tfvars", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }

    }
}
