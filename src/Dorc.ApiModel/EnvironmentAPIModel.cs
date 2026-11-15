using System.Collections.Generic;
using System;

namespace Dorc.ApiModel
{
    public class EnvironmentApiModel
    {
        public int EnvironmentId { get; set; }
        public string EnvironmentName { get; set; }
        public bool EnvironmentSecure { get; set; }
        public bool EnvironmentIsProd { get; set; }
        public bool UserEditable { get; set; }
        public bool IsOwner { get; set; }
        public int? ParentId { get; set; }
        public bool IsParent { get; set; }
        public bool IsEphemeral { get; set; }
        public string EnvironmentTier { get; set; }
        public int? TemplateId { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string EphemeralState { get; set; }
        public EnvironmentDetailsApiModel Details { get; set; }
        public EnvironmentApiModel ParentEnvironment { get; set; }
        public ICollection<EnvironmentApiModel> ChildEnvironments { get; set; }
    }
}