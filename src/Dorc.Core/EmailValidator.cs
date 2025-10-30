using System.Text.RegularExpressions;

namespace Dorc.Core
{
    /// <summary>
    /// Utility class for validating email addresses
    /// </summary>
    public static class EmailValidator
    {
        // RFC 5322 compliant email regex (simplified but robust)
        // Disallows consecutive dots, leading/trailing dots in local part
        private static readonly Regex EmailRegex = new Regex(
            @"^[a-zA-Z0-9!#$%&'*+\/=?^_`{|}~-]+(?:\.[a-zA-Z0-9!#$%&'*+\/=?^_`{|}~-]+)*@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));

        /// <summary>
        /// Validates if the given string is a valid email address
        /// </summary>
        /// <param name="email">The email address to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                return EmailRegex.IsMatch(email);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
