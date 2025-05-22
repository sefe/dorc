using System;

namespace Dorc.ApiModel
{
    public class AccessControlApiModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        [Obsolete("Use Pid, this exists only for backward compatibility with AD")]
        public string Sid { get; set; }
        public int Allow { get; set; }
        public int Deny { get; set; }
        public string Pid { get; set; }
    }
}