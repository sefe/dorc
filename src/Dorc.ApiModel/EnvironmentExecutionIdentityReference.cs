using System;
using System.Text.RegularExpressions;

namespace Dorc.ApiModel
{
    public static class EnvironmentExecutionIdentityReference
    {
        public const int MaxLength = 128;

        private static readonly Regex Pattern =
            new Regex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

        public static string Validate(string executionIdentityReference)
        {
            if (executionIdentityReference == null || executionIdentityReference.Length == 0)
            {
                return null;
            }

            if (executionIdentityReference.Length > MaxLength
                || !Pattern.IsMatch(executionIdentityReference))
            {
                throw new ArgumentException(
                    "ExecutionIdentityReference must be null/empty to clear it, or 1-128 characters "
                    + "using only letters, numbers, '.', '_' and '-'.",
                    nameof(executionIdentityReference));
            }

            return executionIdentityReference;
        }
    }
}
