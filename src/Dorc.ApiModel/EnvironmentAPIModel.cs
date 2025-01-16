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
        public EnvironmentDetailsApiModel Details { get; set; }
        public EnvironmentApiModel ParentEnvironment { get; set; }
        public ICollection<EnvironmentApiModel> ChildEnvironments { get; set; }
    }
}