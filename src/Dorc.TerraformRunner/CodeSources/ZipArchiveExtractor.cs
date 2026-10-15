using System.IO.Compression;
using System.Linq;

namespace Dorc.TerraformRunner.CodeSources
{
    public sealed class ZipArchiveExtractor
    {
        private const int UnixSymlinkFileMode = 0xA000;

        private readonly ZipArchiveExtractionOptions options;

        public ZipArchiveExtractor(ZipArchiveExtractionOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Extract(string archivePath, string targetDirectory)
        {
            if (string.IsNullOrEmpty(archivePath)) throw new ArgumentException("archivePath required", nameof(archivePath));
            if (string.IsNullOrEmpty(targetDirectory)) throw new ArgumentException("targetDirectory required", nameof(targetDirectory));

            Directory.CreateDirectory(targetDirectory);
            var canonicalTarget = NormalizeWithSeparator(Path.GetFullPath(targetDirectory));

            using var archive = ZipFile.OpenRead(archivePath);

            int entryCount = 0;
            long totalBytes = 0L;

            foreach (var entry in archive.Entries)
            {
                ValidateEntryShape(entry);
                var destinationFullPath = ResolveAndContain(canonicalTarget, entry.FullName, entry);

                // Count every entry - directories included - toward the cap.
                // Millions of directory-only entries would otherwise exhaust
                // inodes / the MFT while staying under every byte cap.
                entryCount++;
                if (entryCount > options.MaxEntryCount)
                {
                    throw new UnsafeArchiveException(
                        UnsafeArchiveReason.EntryCountExceeded,
                        entry.FullName,
                        $"archive entry count exceeded {options.MaxEntryCount}");
                }

                bool isDirectoryEntry = string.IsNullOrEmpty(entry.Name);
                if (isDirectoryEntry)
                {
                    Directory.CreateDirectory(destinationFullPath);
                    continue;
                }

                // entry.Length is the DECLARED uncompressed size from the zip
                // central directory - attacker-controlled, so it is only a
                // cheap early reject, never the authoritative cap. The real
                // enforcement is on bytes actually inflated (CopyWithCap
                // below), which also feeds the running total.
                if (entry.Length > options.MaxBytesPerEntry)
                {
                    throw new UnsafeArchiveException(
                        UnsafeArchiveReason.EntrySizeExceeded,
                        entry.FullName,
                        $"entry uncompressed size {entry.Length} exceeded per-entry cap {options.MaxBytesPerEntry}");
                }

                var parent = Path.GetDirectoryName(destinationFullPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                using var sourceStream = entry.Open();
                using var destinationStream = new FileStream(
                    destinationFullPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                // Enforce BOTH the per-entry cap and the remaining total
                // budget against actually-inflated bytes, so a zip that
                // declares Length=0 but inflates to gigabytes is stopped.
                var remainingTotalBudget = options.MaxBytesTotal - totalBytes;
                var effectiveCap = Math.Min(options.MaxBytesPerEntry, remainingTotalBudget);
                var written = CopyWithCap(sourceStream, destinationStream, effectiveCap, entry.FullName,
                    remainingTotalBudget < options.MaxBytesPerEntry);
                totalBytes += written;
            }
        }

        private static void ValidateEntryShape(ZipArchiveEntry entry)
        {
            if (string.IsNullOrEmpty(entry.FullName))
            {
                throw new UnsafeArchiveException(
                    UnsafeArchiveReason.ZeroLengthName,
                    entry.FullName ?? string.Empty,
                    "archive entry has empty name");
            }

            // ExternalAttributes high bits encode unix mode; symlink mode = 0xA000.
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == UnixSymlinkFileMode)
            {
                throw new UnsafeArchiveException(
                    UnsafeArchiveReason.Symlink,
                    entry.FullName,
                    "archive entry is a symlink");
            }
        }

        private static string ResolveAndContain(string canonicalTarget, string entryFullName, ZipArchiveEntry entry)
        {
            // Normalize separators; reject absolute paths up front. Path.IsPathRooted
            // catches Windows drive letters, UNC paths, and forward-slash absolute paths
            // that would cause Path.Combine below to silently discard the target dir.
            var normalized = entryFullName.Replace('\\', '/');
            if (normalized.StartsWith('/')
                || (normalized.Length >= 2 && normalized[1] == ':')
                || Path.IsPathRooted(normalized))
            {
                throw new UnsafeArchiveException(
                    UnsafeArchiveReason.AbsolutePath,
                    entry.FullName,
                    "archive entry uses an absolute path");
            }

            // Reject any explicit '..' segment; canonicalisation will also catch it,
            // but explicit detection produces a clearer error and avoids relying on
            // GetFullPath's behaviour.
            foreach (var _ in normalized.Split('/').Where(segment => segment == ".."))
            {
                throw new UnsafeArchiveException(
                    UnsafeArchiveReason.ParentSegment,
                    entry.FullName,
                    "archive entry contains a parent-directory segment");
            }

            // canonicalTarget ends in a directory separator (NormalizeWithSeparator);
            // normalized has had rooted/absolute forms rejected. String concatenation
            // is safer than Path.Combine here because Path.Combine resets to the second
            // arg if it is rooted.
            var combined = Path.GetFullPath(canonicalTarget + normalized);
            if (!combined.StartsWith(canonicalTarget, StringComparison.Ordinal))
            {
                throw new UnsafeArchiveException(
                    UnsafeArchiveReason.PathOutsideTarget,
                    entry.FullName,
                    "archive entry resolves outside the target directory");
            }
            return combined;
        }

        private static string NormalizeWithSeparator(string fullPath)
        {
            var sep = Path.DirectorySeparatorChar;
            return fullPath.EndsWith(sep) ? fullPath : fullPath + sep;
        }

        // Copies source→destination, aborting if inflated bytes exceed cap.
        // Returns the number of bytes actually written. When capIsTotalBudget
        // is true the cap represents the remaining whole-archive budget, so an
        // overrun is reported as TotalSizeExceeded rather than a per-entry
        // violation.
        private static long CopyWithCap(Stream source, Stream destination, long cap, string entryName, bool capIsTotalBudget)
        {
            var buffer = new byte[81920];
            long copied = 0L;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                copied += read;
                if (copied > cap)
                {
                    throw new UnsafeArchiveException(
                        capIsTotalBudget ? UnsafeArchiveReason.TotalSizeExceeded : UnsafeArchiveReason.EntrySizeExceeded,
                        entryName,
                        capIsTotalBudget
                            ? "archive exceeded total uncompressed byte cap mid-stream"
                            : "entry exceeded per-entry byte cap mid-stream");
                }
                destination.Write(buffer, 0, read);
            }
            return copied;
        }
    }
}
