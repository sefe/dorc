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

        // Default heuristic plus an exact-name match per explicitly flagged
        // property. The request pipeline carries these names on
        // ScriptGroup.SensitivePropertyNames so a `sensitive: true` wizard
        // parameter is redacted even when its name defeats the heuristic.
        public static SensitivePropertyRedactionOptions DefaultWithExactNames(IEnumerable<string> names)
        {
            var patterns = new List<Regex>
            {
                new(DefaultPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant)
            };
            foreach (var name in (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                patterns.Add(new Regex(
                    $"^{Regex.Escape(name)}$",
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
            }
            return new SensitivePropertyRedactionOptions(patterns);
        }
    }
}
