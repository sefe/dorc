using System.Net;
using System.Security;

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
    public sealed class DeploymentCredential
    {
        public DeploymentCredential(string userName, SecureString password)
        {
            UserName = userName;
            Password = password;
        }

        public static DeploymentCredential FromPlainText(string userName, string password)
        {
            return new DeploymentCredential(userName, ToSecureString(password));
        }

        public static SecureString ToSecureString(string value)
        {
            // NetworkCredential already converts a string to a SecureString; there is no need
            // to spell the loop out here.
            var secret = new NetworkCredential(string.Empty, value).SecurePassword;
            secret.MakeReadOnly();
            return secret;
        }

        public string UserName { get; }

        public SecureString Password { get; }

        public bool IsComplete =>
            !string.IsNullOrEmpty(UserName) && Password.Length > 0;
    }
}
