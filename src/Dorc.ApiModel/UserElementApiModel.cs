using System;

namespace Dorc.ApiModel
{
    public class UserElementApiModel
    {
        public string DisplayName { get; set; }
        [Obsolete("Use Pid, this exists only for backward compatibility with AD")]
        public string Sid { get; set; }
        public string Pid { get; set; }
        public string Username { get; set; }
        public bool IsGroup { get; set; }
        public string Email { get; set; }
    }
}
