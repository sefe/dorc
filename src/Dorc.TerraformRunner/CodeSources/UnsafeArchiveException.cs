namespace Dorc.TerraformRunner.CodeSources
{
    public enum UnsafeArchiveReason
    {
        PathOutsideTarget,
        AbsolutePath,
        ParentSegment,
        ZeroLengthName,
        EntryCountExceeded,
        EntrySizeExceeded,
        TotalSizeExceeded,
        Symlink
    }

    public sealed class UnsafeArchiveException : System.IO.IOException
    {
        public UnsafeArchiveReason Reason { get; }

        public string EntryName { get; }

        public UnsafeArchiveException(UnsafeArchiveReason reason, string entryName, string message)
            : base(message)
        {
            Reason = reason;
            EntryName = entryName;
        }
    }
}
