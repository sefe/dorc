using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class TerraformDeploymentOutcomeApiModel
    {
        public int DeploymentResultId { get; set; }
        public long Revision { get; set; }
        [Required]
        public string ApplyOutcome { get; set; }
        [Required]
        public string InventoryOutcome { get; set; }
        [Required]
        public string ArchiveOutcome { get; set; }
        public int RegisteredCount { get; set; }
        public int FailedCount { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public bool InventoryRecoveryAvailable { get; set; }
        [Required]
        public List<string> InventoryWarnings { get; set; } = new List<string>();
    }

    public class TerraformInventorySnapshot
    {
        public int DeploymentResultId { get; set; }
        public bool IsComplete { get; set; }
        public List<CloudResourceApiModel> Resources { get; set; } = new List<CloudResourceApiModel>();
    }

    public class TerraformInventoryObservation
    {
        public int CloudResourceId { get; set; }
        public int? DatabaseId { get; set; }
        public string ResourceIdentifier { get; set; }
    }

    public class TerraformInventoryProvenanceApiModel
    {
        public int ComponentId { get; set; }
        public int EnvironmentId { get; set; }
        public int CloudResourceId { get; set; }
        public int? DatabaseId { get; set; }
        public int FirstSeenResultId { get; set; }
        public int LastSeenResultId { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        [Required]
        public string ResourceIdentifier { get; set; }
        [Required]
        public string ReconciliationState { get; set; }
        public long Revision { get; set; }
    }
}
