namespace Dorc.Monitor.Pipes
{
    /// <summary>
    /// The Windows principal that the Runner process will be started as, and therefore the
    /// only principal besides the Monitor that needs to reach the script-group bundle.
    ///
    /// This is carried from the point where the deployment credential is resolved rather than
    /// assumed to be the Monitor's own account. Today the two usually coincide, which is why
    /// granting only the Monitor's identity has not visibly broken anything - but that is a
    /// coincidence of deployment, not a property of the design, and it stops being true the
    /// moment the deployment credential is made environment-dependent.
    /// </summary>
    public sealed class ScriptGroupReaderIdentity
    {
        public ScriptGroupReaderIdentity(string? domain, string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentException("A script group reader must name an account.", nameof(userName));
            }

            Domain = domain?.Trim() ?? string.Empty;
            UserName = userName.Trim();
        }

        public string Domain { get; }

        public string UserName { get; }

        /// <summary>
        /// The account in the form the local security authority resolves. A user name that
        /// already carries its own qualifier - DOMAIN\user or user@domain - is left alone, so
        /// a configured value that disagrees with the configured domain is not silently
        /// rewritten into a different account.
        /// </summary>
        public string QualifiedAccountName =>
            UserName.Contains('\\') || UserName.Contains('@') || Domain.Length == 0
                ? UserName
                : $@"{Domain}\{UserName}";
    }
}
