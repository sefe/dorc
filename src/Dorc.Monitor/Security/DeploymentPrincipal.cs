using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dorc.Monitor.Security
{
    /// <summary>
    /// The account a deployment's Runner is started as, seen from the Monitor: resolving it,
    /// and refusing the resolutions that would hand a deployment artefact to the host at large.
    /// </summary>
    internal static class DeploymentPrincipal
    {
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

        /// <summary>
        /// An account name is configuration - a row in ConfigValue - so it reaches a log
        /// message as an unvalidated string. A value carrying CR or LF would let whoever set it
        /// forge whole log entries, which is worth stopping for its own sake and is what
        /// CodeQL's log-forging rule flags. Everything else about the value is preserved, so a
        /// misconfigured account is still recognisable in the log that reports it.
        /// </summary>
        public static string SanitizeForLog(string? input)
        {
            return string.IsNullOrEmpty(input)
                ? string.Empty
                : input.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }
    }
}
