namespace Dorc.TerraformRunner.CodeSources
{
    public sealed class ZipArchiveExtractionOptions
    {
        public int MaxEntryCount { get; init; } = 10_000;

        public long MaxBytesPerEntry { get; init; } = 50L * 1024 * 1024;

        public long MaxBytesTotal { get; init; } = 500L * 1024 * 1024;

        public static ZipArchiveExtractionOptions Default => new();
    }
}
