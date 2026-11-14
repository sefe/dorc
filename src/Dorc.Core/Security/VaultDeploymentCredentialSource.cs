using System.Runtime.InteropServices;
using System.Security;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Secrets.OnePassword;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.Security
{
    /// <summary>
    /// Resolves the deployment credential from the secrets vault rather than from DOrc's own
    /// configuration values.
    ///
    /// This is the migration target, and choosing it is what removes the credentials from
    /// DOrc's database altogether. It is not the default: switching where the whole estate's
    /// deployment credentials come from is a deliberate act, not something a release should do
    /// on someone's behalf.
    ///
    /// The vault reader lives in this assembly rather than the API's precisely so that the
    /// Monitor can use it. Whether the Monitor hosts can reach the vault is then a deployment
    /// question with a configured answer, not an architectural dependency — which is the point
    /// of having two implementations rather than moving everything to one.
    /// </summary>
    public class VaultDeploymentCredentialSource : DeploymentCredentialSource
    {
        private readonly IConfigurationSecretsReader _vaultReader;
        private readonly IConfigurationSettings _configuration;

        public VaultDeploymentCredentialSource(
            IConfigurationSecretsReader vaultReader,
            IConfigurationSettings configuration,
            ILogger<VaultDeploymentCredentialSource> logger)
            : base(logger)
        {
            _vaultReader = vaultReader;
            _configuration = configuration;
        }

        public override string Description => "the secrets vault";

        /// <summary>
        /// A named identity is a vault item reference in its own right, so no per-identity
        /// configuration is needed - the environment's reference IS the lookup key, resolved
        /// against exact 1Password item titles ('{identityReference}-username' and
        /// '{identityReference}-password') in the configured vault. This is the variant that
        /// scales to a thousand environments without a thousand config rows.
        /// </summary>
        protected override DeploymentCredential? ResolveNamedIdentity(string identityReference, DeploymentTier tier) =>
            ResolveNamedIdentity(identityReference, tier, CancellationToken.None);

        protected override DeploymentCredential? ResolveNamedIdentity(
            string identityReference,
            DeploymentTier tier,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var principalItemId = $"{identityReference}-username";
            var verificationItemId = $"{identityReference}-password";
            var principalLabel = $"{identityReference} deployment principal";

            var principalIdentifier = _vaultReader is Dorc.Core.Secrets.OnePasswordSecretsReader || cancellationToken.CanBeCanceled
                ? _vaultReader.GetPrincipalIdentifierByItemId(principalItemId, principalLabel, cancellationToken)
                : _vaultReader.GetPrincipalIdentifierByItemId(principalItemId, principalLabel);

            if (IsAbsentPrincipalIdentifier(principalIdentifier))
            {
                Logger.LogError(
                    "Named deployment credential '{IdentityReference}' could not be retrieved from {Source}."
                    + " The username item returned nothing; skipping verification item lookup.",
                    LogText.SingleLine(identityReference),
                    Description);

                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            SecureString? verificationValue = null;
            try
            {
                var verificationLabel = $"{identityReference} deployment verification";
                verificationValue = _vaultReader is Dorc.Core.Secrets.OnePasswordSecretsReader || cancellationToken.CanBeCanceled
                    ? _vaultReader.GetSecureSecret(verificationItemId, verificationLabel, cancellationToken)
                    : _vaultReader.GetSecureSecret(verificationItemId, verificationLabel);

                bool verificationAbsent = IsAbsentVerificationValue(verificationValue);

                if (verificationAbsent)
                {
                    verificationValue?.Dispose();
                    Logger.LogError(
                        "Named deployment credential '{IdentityReference}' could not be retrieved from {Source}."
                        + " The username item returned a value and the verification item returned"
                        + " nothing.",
                        LogText.SingleLine(identityReference),
                        Description);

                    return null;
                }

                return new DeploymentCredential(principalIdentifier.Trim(), verificationValue!);
            }
            catch
            {
                verificationValue?.Dispose();
                throw;
            }
        }

        protected override DeploymentCredential? ResolveTierDefault(DeploymentTier tier) =>
            ResolveTierDefault(tier, CancellationToken.None);

        protected override DeploymentCredential? ResolveTierDefault(
            DeploymentTier tier,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var principalItemId = _configuration.GetDeploymentCredentialItemId(tier, false);
            var verificationItemId = _configuration.GetDeploymentCredentialItemId(tier, true);

            if (string.IsNullOrWhiteSpace(principalItemId) || string.IsNullOrWhiteSpace(verificationItemId))
            {
                // Selected as the source but not told where to look. Refusing is the only safe
                // answer: falling back to configuration values would silently undo the choice to
                // stop keeping deployment credentials in DOrc's database.
                Logger.LogError(
                    "The {Tier} deployment credential is configured to come from {Source}, but its"
                    + " vault item identifiers are not set. Refusing to fall back to configuration"
                    + " values.",
                    tier,
                    Description);

                return null;
            }

            var principalLabel = $"{tier} deployment principal";
            var principalIdentifier = _vaultReader is Dorc.Core.Secrets.OnePasswordSecretsReader || cancellationToken.CanBeCanceled
                ? _vaultReader.GetPrincipalIdentifierByItemId(principalItemId.Trim(), principalLabel, cancellationToken)
                : _vaultReader.GetPrincipalIdentifierByItemId(principalItemId.Trim(), principalLabel);

            if (IsAbsentPrincipalIdentifier(principalIdentifier))
            {
                Logger.LogError(
                    "No {Tier} deployment credential could be retrieved from {Source}."
                    + " The username item returned nothing; skipping verification item lookup.",
                    tier,
                    Description);

                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            SecureString? verificationValue = null;
            try
            {
                var verificationLabel = $"{tier} deployment verification";
                verificationValue = _vaultReader is Dorc.Core.Secrets.OnePasswordSecretsReader || cancellationToken.CanBeCanceled
                    ? _vaultReader.GetSecureSecret(verificationItemId.Trim(), verificationLabel, cancellationToken)
                    : _vaultReader.GetSecureSecret(verificationItemId.Trim(), verificationLabel);

                bool verificationAbsent = IsAbsentVerificationValue(verificationValue);

                if (verificationAbsent)
                {
                    verificationValue?.Dispose();
                    Logger.LogError(
                        "No {Tier} deployment credential could be retrieved from {Source}."
                        + " The username item returned a value and the verification item returned"
                        + " nothing.",
                        tier,
                        Description);

                    return null;
                }

                return new DeploymentCredential(principalIdentifier.Trim(), verificationValue!);
            }
            catch
            {
                verificationValue?.Dispose();
                throw;
            }
        }

        private static bool IsAbsentPrincipalIdentifier(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), OnePasswordItem.MissingFieldSentinel, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAbsentVerificationValue(SecureString? value)
        {
            if (value == null || value.Length == 0)
            {
                return true;
            }

            IntPtr bstr = IntPtr.Zero;
            try
            {
                bstr = Marshal.SecureStringToBSTR(value);
                int length = value.Length;

                int start = 0;
                while (start < length && char.IsWhiteSpace((char)Marshal.ReadInt16(bstr, start * sizeof(char))))
                {
                    start++;
                }

                if (start == length)
                {
                    return true;
                }

                int end = length - 1;
                while (end >= start && char.IsWhiteSpace((char)Marshal.ReadInt16(bstr, end * sizeof(char))))
                {
                    end--;
                }

                int trimmedLength = end - start + 1;
                if (trimmedLength != OnePasswordItem.MissingFieldSentinel.Length)
                {
                    return false;
                }

                for (int i = 0; i < trimmedLength; i++)
                {
                    char actual = char.ToUpperInvariant((char)Marshal.ReadInt16(bstr, (start + i) * sizeof(char)));
                    char sentinel = char.ToUpperInvariant(OnePasswordItem.MissingFieldSentinel[i]);
                    if (actual != sentinel)
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(bstr);
                }
            }
        }
    }
}
