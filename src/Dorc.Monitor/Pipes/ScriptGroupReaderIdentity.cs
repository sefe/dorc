using System.Runtime.Versioning;
using System.Security.Principal;

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

        /// <summary>
        /// Resolves this account to a security identifier, refusing one that names a principal
        /// too broad to be trusted with a deployment artefact.
        ///
        /// Both callers — the script-group bundle and the Terraform plan directory — grant this
        /// principal access to something that carries resolved deployment properties, so both
        /// need the same refusal. A misconfigured account name that happens to resolve to
        /// Authenticated Users would otherwise publish the artefact to the whole host.
        /// </summary>
        /// <param name="refusal">
        /// Why the identity was refused, when this returns false. Suitable for logging: it names
        /// the configured account but nothing from the artefact being protected.
        /// </param>
        [SupportedOSPlatform("windows")]
        public bool TryResolveSecurityIdentifier(
            out SecurityIdentifier? securityIdentifier,
            out string? refusal)
        {
            securityIdentifier = null;
            refusal = null;

            var account = QualifiedAccountName;

            try
            {
                var resolved = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));

                if (IsTooBroadToHoldASecret(resolved))
                {
                    refusal =
                        $"The configured deployment account '{account}' resolves to '{resolved}', which is a"
                        + " group broad enough that granting it access to a deployment artefact would"
                        + " disclose it.";
                    return false;
                }

                securityIdentifier = resolved;
                return true;
            }
            catch (Exception ex)
            {
                refusal =
                    $"The configured deployment account '{account}' could not be resolved to a security"
                    + $" identifier. Exception: {ex}";
                return false;
            }
        }

        /// <summary>
        /// Refuses the principals whose membership is effectively "anyone on the host". This is
        /// a denylist of the catastrophic cases, not a general test for whether a SID names a
        /// group — that needs the account's SID_NAME_USE, which has no managed API. A narrower
        /// group slipping through still only reaches one deployment's artefacts, for the seconds
        /// before the logon fails on the same misconfigured name.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static bool IsTooBroadToHoldASecret(SecurityIdentifier sid)
        {
            return sid.IsWellKnown(WellKnownSidType.WorldSid)
                || sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)
                || sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid)
                || sid.IsWellKnown(WellKnownSidType.BuiltinGuestsSid)
                || sid.IsWellKnown(WellKnownSidType.InteractiveSid)
                || sid.IsWellKnown(WellKnownSidType.NetworkSid)
                || sid.IsWellKnown(WellKnownSidType.AnonymousSid);
        }
    }
}
