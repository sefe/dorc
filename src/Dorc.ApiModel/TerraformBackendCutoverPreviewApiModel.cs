using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Read-only, admin-only analysis of what an empty-backend cutover for one
    /// project/environment would require. It mirrors the server guards without
    /// changing anything, so an operator can review before submitting.
    /// </summary>
    public sealed class TerraformBackendCutoverPreviewApiModel
    {
        public string Revision { get; set; }
        public int EnvironmentId { get; set; }
        public string EnvironmentName { get; set; }

        /// <summary>The backend currently effective for this environment; null when none is configured.</summary>
        public TerraformStateBackendApiModel CurrentBackend { get; set; }

        /// <summary>
        /// Terminal requests in scope that predate backend pinning and therefore have no
        /// state evidence. Every one must be acknowledged in <see cref="TerraformBackendCutoverApiModel.LegacyRetirement"/>.
        /// </summary>
        public List<TerraformLegacyRequestApiModel> LegacyRequests { get; set; } = new List<TerraformLegacyRequestApiModel>();

        /// <summary>Pinned Catalog requests that would be retired alongside the cutover.</summary>
        public int PinnedRequestCount { get; set; }

        /// <summary>
        /// Conditions that would make the cutover fail, in the server's own words. Empty means
        /// the cutover can be submitted once the operator has completed the container verification.
        /// </summary>
        public List<string> Blockers { get; set; } = new List<string>();
    }

    public sealed class TerraformLegacyRequestApiModel
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset? RequestedTime { get; set; }
        public string Components { get; set; }
    }
}
