namespace Dorc.ApiModel
{
    public sealed class TerraformBackendCutoverApiModel
    {
        public string Revision { get; set; }
        public int EnvironmentId { get; set; }
        public TerraformStateBackendApiModel ExpectedBackend { get; set; }
        public TerraformStateBackendApiModel Backend { get; set; }
        public bool ConfirmBothContainersEmpty { get; set; }
        public string VerificationSha256 { get; set; }
    }
}
