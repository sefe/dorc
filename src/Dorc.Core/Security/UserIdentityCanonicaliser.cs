namespace Dorc.Core.Security
{
    /// <summary>
    /// Reduces the several identity forms DOrc stores and receives to a single
    /// comparable value.
    ///
    /// DOrc records a user's identity with <c>GetUserFullDomainName</c>, whose result is
    /// authentication-scheme dependent. Windows authentication yields <c>DOMAIN\user</c>;
    /// OAuth yields a fallback chain of email, then SAM account name, then identity name;
    /// machine-to-machine callers yield a client id. Both schemes are registered and
    /// selected per request, so the same person submitting under one and acting under the
    /// other is recorded under two different strings.
    ///
    /// Comparing those strings directly would report two identities as different when they
    /// are the same person. Where the comparison guards a segregation-of-duties rule, that
    /// failure permits the very self-approval the rule exists to prevent — so the
    /// canonicalisation here deliberately errs toward reporting identities as the *same*:
    /// an over-match refuses an action, an under-match allows one.
    /// </summary>
    public static class UserIdentityCanonicaliser
    {
        /// <summary>
        /// Reduces an identity to its account name, discarding the domain or realm that
        /// varies by authentication scheme. Returns an empty string when no account name
        /// can be established, which callers must treat as "identity unknown" rather than
        /// as "not a match".
        /// </summary>
        public static string Canonicalise(string? identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return string.Empty;
            }

            var value = identity.Trim();

            // DOMAIN\user, or the UPN-suffixed forms of it, reduce to the account name.
            var lastBackslash = value.LastIndexOf('\\');
            if (lastBackslash >= 0)
            {
                value = value[(lastBackslash + 1)..];
            }

            // user@domain reduces to the account name. Applied after the backslash strip so
            // that DOMAIN\user@realm is handled. An '@' at position zero yields an empty
            // account name, which is the correct answer - there is no account name to
            // establish - so the split is not guarded against it.
            var firstAt = value.IndexOf('@');
            if (firstAt >= 0)
            {
                value = value[..firstAt];
            }

            return value.Trim();
        }

        /// <summary>
        /// True when both identities canonicalise to the same non-empty account name.
        ///
        /// An empty canonical form on either side returns <c>false</c>, because "unknown"
        /// is not "equal". Callers that need to fail closed on an unestablished identity
        /// must test for that separately — see <see cref="CanEstablish"/> — rather than
        /// relying on this method's result.
        /// </summary>
        public static bool SamePrincipal(string? left, string? right)
        {
            var canonicalLeft = Canonicalise(left);
            var canonicalRight = Canonicalise(right);

            if (canonicalLeft.Length == 0 || canonicalRight.Length == 0)
            {
                return false;
            }

            // AD account names and email local parts are case-insensitive.
            return string.Equals(canonicalLeft, canonicalRight, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when an identity reduces to a usable account name. A caller enforcing a
        /// rule about *who* someone is must refuse when this is false: it cannot establish
        /// that the two parties differ, and permitting on that basis would fail open.
        /// </summary>
        public static bool CanEstablish(string? identity)
        {
            return Canonicalise(identity).Length > 0;
        }
    }
}
