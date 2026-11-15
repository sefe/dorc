using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Security;

namespace Dorc.Monitor.Security
{
    /// <summary>
    /// Expands deployment property tokens (${Name}, $Name$, $Name) in candidate script paths
    /// and resolves them against a root using Windows/UNC semantics on every host OS.
    /// </summary>
    internal static class PropertyTokenExpander
    {
        private const int MaxExpansionPasses = 4;

        private static readonly Regex ValidPropertyNamePattern = new(
            @"^[A-Za-z0-9_.-]+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex DropFolderTokenPattern = new(
            @"(?:\$\{DropFolder\}|\$DropFolder\$|\$DropFolder(?![A-Za-z0-9_.-]))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Expand(string? path, IDictionary<string, VariableValue>? properties)
        {
            if (string.IsNullOrEmpty(path) || properties == null || properties.Count == 0)
            {
                return path ?? string.Empty;
            }

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in properties)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !ValidPropertyNamePattern.IsMatch(pair.Key))
                {
                    continue;
                }

                var text = pair.Value?.Value?.ToString();
                if (text != null)
                {
                    lookup[pair.Key] = text;
                }
            }

            if (lookup.Count == 0)
            {
                return path;
            }

            var orderedKeys = lookup.Keys
                .OrderByDescending(k => k.Length)
                .ToArray();

            var current = path;
            for (var pass = 0; pass < MaxExpansionPasses && current.Contains('$'); pass++)
            {
                var next = current;
                foreach (var key in orderedKeys)
                {
                    var replacement = lookup[key];
                    var escapedKey = Regex.Escape(key);
                    var pattern = $@"(?:\$\{{{escapedKey}\}}|\${escapedKey}\$|\${escapedKey}(?![A-Za-z0-9_.-]))";
                    next = Regex.Replace(
                        next,
                        pattern,
                        _ => replacement,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }

                if (string.Equals(next, current, StringComparison.Ordinal))
                {
                    break;
                }

                current = next;
            }

            return current;
        }

        public static bool ContainsUnexpandedDropFolderToken(string? path)
        {
            return !string.IsNullOrEmpty(path) && DropFolderTokenPattern.IsMatch(path);
        }

        public static string ResolveAgainstRoot(string root, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return root;
            }

            var trimmed = candidate.Trim();
            if (trimmed[0] == '\\'
                || trimmed[0] == '/'
                || (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
                || SourceHost.IsRemoteOrUnc(trimmed)
                || PathConfinement.Canonicalise(trimmed) != null)
            {
                return trimmed;
            }

            return (root ?? string.Empty).TrimEnd('\\', '/') + '\\' + trimmed;
        }
    }
}
