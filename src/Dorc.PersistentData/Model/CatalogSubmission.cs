namespace Dorc.PersistentData.Model;

public class CatalogSubmission
{
    public string CallerHash { get; set; } = string.Empty;
    public Guid SubmissionKey { get; set; }
    public byte[] PayloadSalt { get; set; } = Array.Empty<byte>();
    public byte[] PayloadFingerprint { get; set; } = Array.Empty<byte>();
    public int DeploymentRequestId { get; set; }
    public string ComponentJson { get; set; } = string.Empty;
}
