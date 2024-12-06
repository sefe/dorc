using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public enum AccessControlType
    {
        Project,
        Environment
    }

    public class AccessSecureApiModel
    {
        public AccessControlType Type { get; set; }
        public string Name { get; set; }
        public Guid ObjectId { get; set; }
        public bool UserEditable { get; set; }
        public IEnumerable<AccessControlApiModel> Privileges { get; set; }
    }
}