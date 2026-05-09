# SPEC: S-002 — Safe ZIP extraction primitive

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | DRAFT (executing) |
| **IS step**| S-002                                |
| **SC**     | SC-06                                |

## Cohesion-first name

Candidates considered (per CLAUDE.md banned-list rule):
- `SafeZipExtractor` — borderline; `Safe` is a quality, not a domain.
- `BoundedZipExtraction` — fine, but `Extraction` is verb-y.
- `ProtectedZipReader` — `Reader` connotes streaming; this writes to disk.
- **`ZipArchiveExtractor`** — neutral, accurate. Single responsibility: extract a zip archive to a target directory subject to caps and validation. **CHOSEN.**

The exception type: `UnsafeArchiveException` — naming after the *condition* (unsafe archive) is more cohesive than after the operation (extraction failed). **CHOSEN.**

## Surface

```
namespace Dorc.TerraformRunner.CodeSources

public sealed class ZipArchiveExtractionOptions
    MaxEntryCount: int (default 10_000)
    MaxBytesPerEntry: long (default 50 MiB)
    MaxBytesTotal: long (default 500 MiB)

public sealed class UnsafeArchiveException : InvalidDataException
    Reason: enum { PathOutsideTarget, AbsolutePath, ParentSegment,
                   ZeroLengthName, EntryCountExceeded,
                   EntrySizeExceeded, TotalSizeExceeded, Symlink }

public sealed class ZipArchiveExtractor
    ctor(ZipArchiveExtractionOptions)
    Extract(string archivePath, string targetDirectory)
        - opens archive read-only
        - iterates entries; for each:
            - reject zero-length name
            - reject absolute path / paths starting with /, \, drive letter
            - canonicalize fullEntryPath = Path.GetFullPath(target/entry.FullName)
              and require it starts with target+separator (containment)
            - reject if entry.ExternalAttributes implies symlink
            - if directory entry, mkdir; if file, count it and check
              cumulative caps, then copy stream with a per-entry byte cap
        - if any cap or rule violated → throw UnsafeArchiveException(Reason);
          target directory may contain partial state; caller is responsible
          for choosing a fresh target dir before extraction (the existing
          call sites already do)
```

## Test fixtures

A test helper builds in-memory zip archives via `ZipArchive` for each scenario; tests do not rely on committed binary fixtures.

1. Normal archive (one file, one folder, normal contents) → extracts; files exist on disk; total bytes match.
2. Path-traversal entry (`../etc/passwd`) → throws `UnsafeArchiveException(ParentSegment)`.
3. Absolute-path entry (`/etc/foo`) → throws `UnsafeArchiveException(AbsolutePath)`.
4. Zero-length name entry → throws `UnsafeArchiveException(ZeroLengthName)`.
5. Entry count exceeded (cap=2, archive has 3 entries) → throws `EntryCountExceeded`.
6. Per-entry size exceeded (cap=10 bytes, entry has 20) → throws `EntrySizeExceeded`.
7. Total size exceeded → throws `TotalSizeExceeded`.
8. Symlink entry (`ExternalAttributes` set to indicate Unix symlink) → throws `Symlink`. (May be skipped on platforms where the test cannot create such an entry; documented.)
9. Containment via canonical path: an entry named `subdir/../escape.txt` that *normalizes* to escape outside target → caught by `Path.GetFullPath` containment check.

## Wiring

S-002 also replaces the two `ZipFile.ExtractToDirectory` call sites in `AzureArtifactCodeSourceProvider.cs:218` and `:262` with calls to the new extractor. The wiring is part of S-002 (small, atomic) rather than carried to a later step.

## Out of scope

- Tar (`.tar`, `.tar.gz`) extraction — not used by current providers; revisit if needed.
- Streaming download → extract pipeline — current code stages to a temp file first; preserve this.
