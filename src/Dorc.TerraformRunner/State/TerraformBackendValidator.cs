using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.State
{
    // Detects user-checked-in backend blocks. DOrc owns the backend
    // configuration; user-supplied backend declarations would race the
    // platform-rendered _dorc_backend.tf and produce undefined behaviour.
    // Per HLPS SC-01, we reject pre-flight with a precise error string.
    public static class TerraformBackendValidator
    {
        // Matches `terraform { backend "<kind>" { ... }` blocks. Tolerant of
        // whitespace and comments; deliberately conservative.
        private static readonly Regex BackendBlockRegex = new(
            @"terraform\s*\{[^}]*backend\s*""[^""]+""",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public sealed record Finding(string FilePath, int LineNumber);

        public static IReadOnlyList<Finding> Scan(string workingDirectory)
        {
            if (string.IsNullOrEmpty(workingDirectory)) throw new ArgumentException("workingDirectory required", nameof(workingDirectory));
            if (workingDirectory.Contains(".."))
            {
                throw new ArgumentException("workingDirectory must not contain parent-directory segments", nameof(workingDirectory));
            }
            if (!Directory.Exists(workingDirectory)) return Array.Empty<Finding>();

            var findings = new List<Finding>();
            foreach (var file in Directory.EnumerateFiles(workingDirectory, "*.tf", SearchOption.AllDirectories))
            {
                // Skip the platform-rendered file itself if it ever ends up
                // inside the working dir during a re-scan.
                if (Path.GetFileName(file) == TerraformBackendRenderer.BackendFileName) continue;

                var content = File.ReadAllText(file);
                var match = BackendBlockRegex.Match(content);
                if (match.Success)
                {
                    var lineNumber = 1 + content.Substring(0, match.Index).Count(c => c == '\n');
                    findings.Add(new Finding(file, lineNumber));
                }
            }
            return findings;
        }

        public static void RejectIfUserBackendBlocksPresent(string workingDirectory)
        {
            var findings = Scan(workingDirectory);
            if (findings.Count > 0)
            {
                var first = findings[0];
                var detail = string.Join("; ",
                    findings.Select(f => $"{Path.GetFileName(f.FilePath)}:{f.LineNumber}"));
                throw new InvalidOperationException(
                    "DOrc owns the Terraform backend configuration; user-checked-in " +
                    "`terraform { backend ... }` blocks are not permitted. Remove the " +
                    $"backend block from {Path.GetFileName(first.FilePath)} (line {first.LineNumber}). " +
                    $"All offending files: {detail}");
            }
        }
    }
}
