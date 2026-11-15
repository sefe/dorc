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
    public sealed class DeploymentCredential : IDisposable
    {
        public DeploymentCredential(string userName, SecureString password)
        {
            ArgumentNullException.ThrowIfNull(userName);
            ArgumentNullException.ThrowIfNull(password);
            UserName = userName;
            Password = password;
        }

        public static DeploymentCredential FromPlainText(string userName, string password)
        {
            var secret = ToSecureString(password);
            try
            {
                return new DeploymentCredential(userName, secret);
            }
            catch
            {
                secret.Dispose();
                throw;
            }
        }

        public static SecureString ToSecureString(string? value)
        {
            var secret = new SecureString();
            try
            {
                if (!string.IsNullOrEmpty(value))
                {
                    foreach (char ch in value)
                    {
                        secret.AppendChar(ch);
                    }
                }

                secret.MakeReadOnly();
                return secret;
            }
            catch
            {
                secret.Dispose();
                throw;
            }
        }

        public string UserName { get; }

        public SecureString Password { get; }

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(UserName) && Password.Length > 0;

        public void Dispose()
        {
            Password.Dispose();
        }

        /// <summary>
        /// The account in the form the local security authority resolves. A user name that
        /// already carries its own qualifier - DOMAIN\user or user@domain - is left alone, so
        /// a configured value that disagrees with the configured domain is not silently
        /// rewritten into a different account.
        /// </summary>
        public string QualifiedAccountName(string? domain)
        {
            var userName = UserName.Trim();
            var trimmedDomain = domain?.Trim() ?? string.Empty;

            return userName.Contains('\\') || userName.Contains('@') || trimmedDomain.Length == 0
                ? userName
                : $@"{trimmedDomain}\{userName}";
        }
    }
}
