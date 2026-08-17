namespace Dorc.ApiModel
{
    /// <summary>
    /// A principal returned by a directory lookup: a user, a group, or a service principal
    /// (machine client). Replaces DirectoryPrincipalApiModel, which named only one of the three.
    /// </summary>
    public class DirectoryPrincipalApiModel
    {
        public string DisplayName { get; set; }

        /// <summary>
        /// Entra object id for users and groups; the application (client) id for service
        /// principals, because that is the value AccessControl.Pid holds for machine callers.
        /// Was named Pid. NOT called ObjectId: AccessControl.ObjectId already means "the id of
        /// the secured object", and the two appear in the same expressions.
        /// </summary>
        public string PrincipalId { get; set; }

        /// <summary>
        /// On-premises security identifier for directory-synced principals, when Entra exposes
        /// it. Null for cloud-only principals. Matched against AccessControl.Sid so grants made
        /// before the Graph migration keep resolving. Was named Sid.
        /// </summary>
        public string OnPremisesSid { get; set; }

        /// <summary>
        /// On-premises sAMAccountName for directory-synced principals, when Entra exposes it.
        /// Null for cloud-only accounts. Used to build DOMAIN\logon names matching what Active
        /// Directory produced before the migration.
        /// </summary>
        public string SamAccountName { get; set; }

        public string Username { get; set; }
        public bool IsGroup { get; set; }
        public string Email { get; set; }
    }
}
