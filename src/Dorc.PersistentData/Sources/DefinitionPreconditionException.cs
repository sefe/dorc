namespace Dorc.PersistentData.Sources;

public sealed class DefinitionPreconditionException : InvalidOperationException
{
    public int StatusCode { get; }
    public DefinitionPreconditionException(bool missing)
        : base(missing ? "An expected revision is required. Reload before saving."
            : "The definition or its consumers changed. Reload and review before saving.")
    {
        StatusCode = missing ? 428 : 409;
    }
}
