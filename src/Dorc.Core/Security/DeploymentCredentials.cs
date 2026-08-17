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
        /// The credential for an environment: its own execution identity where it names one,
        /// and the tier default where it does not.
        ///
        /// Execution identity was a boolean — production or not — which is why one compromised
        /// deployment reaches the whole estate. An environment may now name its own identity,
        /// and one that does not behaves exactly as before, so migration proceeds environment by
        /// environment rather than as a flag day.
        /// </summary>
        /// <param name="identityReference">
        /// The environment's identity reference, or null/empty for the tier default.
        /// </param>
        DeploymentCredential? Resolve(DeploymentTier tier, string? identityReference);

        /// <summary>
        /// How many resolutions have fallen back to the tier default, and how many used an
        /// environment's own identity. Migration progress is otherwise invisible, and "we have
        /// started binding identities" is not the same claim as "the estate is bound".
        /// </summary>
        (int Bound, int Fallback) ResolutionCounts { get; }

        /// <summary>
        /// Where this implementation reads from, for logging. Which source is in force is
        /// otherwise invisible, and a deployment failing to authenticate is a great deal easier
        /// to diagnose when the log says where the credential was looked for.
        /// </summary>
        string Description { get; }
    }
}
