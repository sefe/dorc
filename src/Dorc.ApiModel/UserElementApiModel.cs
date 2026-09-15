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

        /// <summary>
        /// On-premises sAMAccountName for directory-synced principals, when Entra exposes it.
        /// Null for cloud-only accounts. Used to build DOMAIN\logon names that match what
        /// Active Directory produced before the Graph migration.
        /// </summary>
        public string SamAccountName { get; set; }
    }
}
