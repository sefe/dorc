namespace Dorc.PersistentData.Sources;

public sealed class CatalogSubmissionConflictException : InvalidOperationException
{
    public CatalogSubmissionConflictException()
        : base("This submission key was already used with a different payload. Start a new logical submission.") { }
}
