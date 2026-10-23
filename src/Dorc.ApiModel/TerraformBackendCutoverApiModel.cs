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

        /// <summary>
        /// Optional, admin-only acknowledgement that requests predating backend pinning
        /// (so having no state evidence) are being retired. Omit unless the project has
        /// such requests; when present every legacy request must be listed explicitly.
        /// </summary>
        public TerraformLegacyRequestRetirementApiModel LegacyRetirement { get; set; }
    }

    public sealed class TerraformLegacyRequestRetirementApiModel
    {
        /// <summary>Exact set of legacy request IDs the operator reviewed; must match the server's view.</summary>
        public System.Collections.Generic.List<int> RequestIds { get; set; }

        /// <summary>
        /// What was done about any infrastructure those requests applied without durable state
        /// (for example destroyed by hand, imported into a new state, or abandoned). Recorded verbatim.
        /// </summary>
        public string StateDisposition { get; set; }
    }
}
