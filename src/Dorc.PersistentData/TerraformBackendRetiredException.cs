namespace Dorc.PersistentData;

public sealed class TerraformBackendRetiredException() : InvalidOperationException(
    "This request's Terraform backend was retired by an audited cutover. Submit a new request and plan; the original snapshot cannot be replayed.");
