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

        /// <summary>
        /// The execution identity this environment deploys under, or null/empty to use the tier
        /// default. Names a credential rather than carrying one.
        ///
        /// Not annotated nullable: this assembly is shared with the .NET Framework runner and
        /// compiles at C# 7.3.
        /// </summary>
        public string ExecutionIdentityReference { get; set; }
        public bool UserEditable { get; set; }
        public bool IsOwner { get; set; }
        public int? ParentId { get; set; }
        public bool IsParent { get; set; }
        public EnvironmentDetailsApiModel Details { get; set; }
        public EnvironmentApiModel ParentEnvironment { get; set; }
        public ICollection<EnvironmentApiModel> ChildEnvironments { get; set; }
    }
}