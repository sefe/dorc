using Dorc.ApiModel;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;

namespace Dorc.Monitor.Security
{
    /// <summary>
    /// The outcome of resolving a configured account name to a principal that may be admitted
    /// to a deployment artefact: either the identifier, or the reason it was refused.
    /// </summary>
    public sealed class DeploymentPrincipalResolution
    {
        private DeploymentPrincipalResolution(SecurityIdentifier? securityIdentifier, string? refusal, Exception? cause)
        {
            SecurityIdentifier = securityIdentifier;
            Refusal = refusal;
            Cause = cause;
        }

        /// <summary>The resolved identifier, or null when the account was refused.</summary>
        public SecurityIdentifier? SecurityIdentifier { get; }

        /// <summary>
        /// Why the account was refused, when it was. Names the configured account and nothing
        /// from the artefact being protected, so it is safe to log.
        /// </summary>
        public string? Refusal { get; }

        /// <summary>The exception behind the refusal, when there was one.</summary>
        public Exception? Cause { get; }

        public bool IsResolved => SecurityIdentifier != null;

        public static DeploymentPrincipalResolution Resolved(SecurityIdentifier securityIdentifier) =>
            new DeploymentPrincipalResolution(securityIdentifier, null, null);

        public static DeploymentPrincipalResolution Refused(string refusal, Exception? cause = null) =>
            new DeploymentPrincipalResolution(null, refusal, cause);
    }

    /// <summary>
    /// The account a deployment's Runner is started as, seen from the Monitor: resolving it,
    /// and refusing the resolutions that would hand a deployment artefact to the host at large.
    ///
    /// Every artefact the Monitor writes for a Runner - the script group bundle, the pipe and
    /// the Terraform plan directory - admits this principal, so every one of them needs the
    /// same resolution and the same refusals. This is the one place they are made.
    /// </summary>
    internal static class DeploymentPrincipal
    {
        /// <summary>
        /// Resolves a configured account name to a security identifier, refusing one that
        /// names a principal too broad to be trusted with a deployment artefact and one the
        /// local security authority cannot resolve at all.
        ///
        /// A refusal is returned rather than thrown because the grants this feeds only ever
        /// WIDEN access to an artefact whose confinement is already in place. A transient
        /// directory-service failure can leave the artefact unreadable but never over-readable,
        /// and the deployment fails moments later on the logon that uses the same name, so a
        /// refusal here is reported by the caller and not made fatal.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static DeploymentPrincipalResolution Resolve(string account)
        {
            try
            {
                var sid = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));

                if (IsTooBroadToHoldASecret(sid))
                {
                    return DeploymentPrincipalResolution.Refused(
                        $"The configured deployment account '{LogText.SingleLine(account)}' resolves to '{sid.Value}',"
                        + " which is a group broad enough that admitting it to a deployment artefact would disclose it.");
                }

                return DeploymentPrincipalResolution.Resolved(sid);
            }
            catch (IdentityNotMappedException ex) { return Unresolved(account, ex); }
            catch (SecurityException ex) { return Unresolved(account, ex); }
            catch (SystemException ex) when (ex is not OutOfMemoryException)
            {
                // NTAccount.Translate reports a directory-service failure as a plain
                // SystemException, so the typed catches above do not cover the case this exists
                // to survive: an unreachable domain controller.
                return Unresolved(account, ex);
            }
        }

        private static DeploymentPrincipalResolution Unresolved(string account, Exception cause) =>
            DeploymentPrincipalResolution.Refused(
                $"The configured deployment account '{LogText.SingleLine(account)}' could not be resolved to a"
                + " security identifier.",
                cause);

        /// <summary>
        /// Refuses the principals whose membership is effectively "anyone on the host". This is
        /// a denylist of the catastrophic cases, not a general test for whether a SID names a
        /// group - that needs the account's SID_NAME_USE, which has no managed API. A narrower
        /// group slipping through still only reaches one deployment's artefacts, for the
        /// seconds before the logon fails on the same misconfigured name.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static bool IsTooBroadToHoldASecret(SecurityIdentifier sid)
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
