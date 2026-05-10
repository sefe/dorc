using System.Linq;
using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.State
{
    // Detects user-checked-in backend blocks. DOrc owns the backend
    // configuration; user-supplied backend declarations would race the
    // platform-rendered _dorc_backend.tf and produce undefined behaviour.
    // Per , we reject pre-flight with a precise error string.
    public static class TerraformBackendValidator
    {
        // Matches the start of a `terraform {` block. We then scan its
        // balanced-brace body for `backend "<kind>"` declarations. A naive
        // single-regex approach (`terraform\s*\{[^}]*backend ...`) fails on
        // realistic .tf files where required_providers { ... } precedes the
        // backend declaration: the [^}]* stops at the first inner }, so the
        // backend block goes undetected.
        private static readonly Regex TerraformBlockOpenRegex = new(
            @"\bterraform\s*\{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex BackendDeclarationRegex = new(
            @"\bbackend\s*""[^""]+""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            // Filter out the platform-rendered file in the source sequence so
            // re-scans don't flag DOrc's own backend file.
            var inputFiles = Directory
                .EnumerateFiles(workingDirectory, "*.tf", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != TerraformBackendRenderer.BackendFileName);

            foreach (var file in inputFiles)
            {
                var content = File.ReadAllText(file);
                var backendIndex = FindBackendInsideTerraformBlock(content);
                if (backendIndex >= 0)
                {
                    var lineNumber = 1 + content.Substring(0, backendIndex).Count(c => c == '\n');
                    findings.Add(new Finding(file, lineNumber));
                }
            }
            return findings;
        }

        // Returns the character index of the `backend "<kind>"` declaration
        // inside the first `terraform { ... }` block that contains one, or
        // -1 if none. Brace-balanced scan; tolerates nested blocks (e.g.
        // required_providers { ... }) appearing before or after the backend.
        // Strings, comments, and heredocs are not stripped - false positives
        // from those constructs are extremely unlikely in practice and would
        // only over-reject (which is acceptable).
        private static int FindBackendInsideTerraformBlock(string content)
        {
            foreach (var bodyStart in TerraformBlockOpenRegex.Matches(content).Cast<Match>()
                .Select(open => open.Index + open.Length))
            {
                int bodyEnd = FindMatchingClose(content, bodyStart);
                if (bodyEnd < 0) continue;

                var body = content.Substring(bodyStart, bodyEnd - bodyStart);
                var backend = BackendDeclarationRegex.Match(body);
                if (backend.Success)
                {
                    return bodyStart + backend.Index;
                }
            }
            return -1;
        }

        private static int FindMatchingClose(string content, int afterOpenBrace)
        {
            int depth = 1;
            for (int i = afterOpenBrace; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
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
