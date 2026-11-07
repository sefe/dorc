using System.Text.RegularExpressions;

namespace Dorc.PersistentData.Sources
{
    // Normalises the optional project naming-standard codes that seed Terraform
    // catalog input suggestions. Codes are stored lowercase; blanks become null.
    public static class ProjectNamingCodes
    {
        private static readonly Regex BusinessVerticalShape = new("^[a-z]{2}$", RegexOptions.Compiled);
        private static readonly Regex ApplicationCodeShape = new("^[a-z0-9]{3}$", RegexOptions.Compiled);

        public static string? NormaliseBusinessVertical(string? value)
        {
            var code = Normalise(value);
            if (code != null && !BusinessVerticalShape.IsMatch(code))
                throw new ArgumentException("Business vertical must be a two-letter code (for example \"tr\").");
            return code;
        }

        public static string? NormaliseApplicationCode(string? value)
        {
            var code = Normalise(value);
            if (code != null && !ApplicationCodeShape.IsMatch(code))
                throw new ArgumentException("Application code must be a three-character code from the IT application code register.");
            return code;
        }

        private static string? Normalise(string? value)
        {
            var trimmed = value?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
