using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.Logging
{
    public sealed class SensitivePropertyRedactionOptions
    {
        public const string DefaultPattern = "(?i)(token|pat|secret|password|key|connectionstring)";

        public IReadOnlyList<Regex> Patterns { get; }

        public SensitivePropertyRedactionOptions(IEnumerable<Regex> patterns)
        {
            if (patterns is null) throw new ArgumentNullException(nameof(patterns));
            Patterns = patterns.ToList();
        }

        public static SensitivePropertyRedactionOptions Default()
            => new(new[] { new Regex(DefaultPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant) });
    }
}
