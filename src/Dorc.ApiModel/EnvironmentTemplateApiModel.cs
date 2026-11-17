using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class EnvironmentTemplateApiModel
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string DefaultTier { get; set; } = "ephemeral";
        public int? BaseEnvironmentId { get; set; }
        public string BaseEnvironmentName { get; set; }
        public int DefaultTtlMinutes { get; set; } = 240;
        public int MaxTtlMinutes { get; set; } = 1440;
        public IList<int> ProvisionComponentIds { get; set; } = new List<int>();
        public IList<int> ValidationComponentIds { get; set; } = new List<int>();
        public string TierParameterOverridesJson { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
