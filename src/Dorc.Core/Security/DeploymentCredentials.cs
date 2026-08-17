namespace Dorc.Core.Security
{
    /// <summary>
    /// Which population a deployment belongs to, and therefore which credential pair it runs
    /// under.
    ///
    /// A tier, not a boolean. The estate holds well over a thousand environments, so an account
    /// per environment is not viable — but a boolean cannot express anything between "all of
    /// production" and "everything else", and four separate sites currently re-derive that
    /// boolean from scratch.
    /// </summary>
    public enum DeploymentTier
    {
        NonProduction,
        Production
    }

    /// <summary>
    /// The account a deployment executes as.
    /// </summary>
    /// <remarks>
    /// The password is a plain string, which is a known weakness rather than an oversight: it
    /// cannot be zeroed and the garbage collector may copy it. Fixing that belongs at the
    /// storage layer, where the value is decrypted, and is recorded against the credential
    /// storage step. Introducing a different representation here without changing the layer
    /// underneath would move the copies around rather than remove them.
    /// </remarks>
    public sealed class DeploymentCredential
    {
        public DeploymentCredential(string userName, string password)
        {
            UserName = userName;
            Password = password;
        }

        public string UserName { get; }

        public string Password { get; }

        public bool IsComplete =>
            !string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password);
    }

    /// <summary>
    /// Resolves the account a deployment executes as.
    ///
    /// Four sites across two processes resolved this independently, each with its own copy of
    /// the same four configuration-value key names and the same production boolean: the
    /// PowerShell dispatcher, the Terraform dispatcher, the daemon status probe running in the
    /// API process, and the password reset controller. Four copies of a security decision is
    /// four places for it to drift, and it is why the probe could be reached without
    /// authorization while the dispatchers could not.
    ///
    /// Putting resolution behind one abstraction has a second purpose beyond deduplication: it
    /// makes where credentials come from a deployment-time choice rather than a design
    /// dependency. One implementation reads configuration values, as today; another reads a
    /// secrets vault. Selecting the vault-backed one removes vault reachability from the Monitor
    /// hosts as an architectural requirement — it becomes configuration.
    /// </summary>
    public interface IDeploymentCredentialSource
    {
        /// <summary>
        /// The credential for this tier, or null when it cannot be resolved. Null is a refusal
        /// to guess, not an error to swallow: a caller that proceeds without a credential
        /// authenticates as whatever the host happens to be running as.
        /// </summary>
        DeploymentCredential? Resolve(DeploymentTier tier);

        /// <summary>
        /// Where this implementation reads from, for logging. Which source is in force is
        /// otherwise invisible, and a deployment failing to authenticate is a great deal easier
        /// to diagnose when the log says where the credential was looked for.
        /// </summary>
        string Description { get; }
    }
}
