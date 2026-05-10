using System.Text.RegularExpressions;

namespace Tools.TerraformLint;

// Two lint commands for the Terraform stock-module catalog space.
//
//   manifest-sensitive-names <manifests-dir>
//     Iterates every YAML in the supplied manifests directory; for every
//     parameter whose name matches a configurable secret-shape substring
//     (case-insensitive), emits a `::warning::` annotation if the parameter
//     is not flagged `sensitive: true`. Never fails the build — surfaces
//     producer omissions for human review.
//
//   secret-outputs <stock-modules-root>
//     Walks every outputs.tf under the supplied root; for every
//     `output "name" { ... }` block whose name matches a built-in secret
//     pattern (token / pat / secret / password / key / connectionstring),
//     fails the build if the body declares `sensitive = false` or omits
//     `sensitive = true` entirely. Forces module authors to be explicit.
//
//   --self-test
//     Exercises both commands against synthetic fixtures in a temp dir.
//
// Usage:
//   Tools.TerraformLint manifest-sensitive-names <manifests-dir>
//   Tools.TerraformLint secret-outputs <stock-modules-root>
//   Tools.TerraformLint --self-test
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }
        switch (args[0])
        {
            case "--self-test":
                return SelfTest.Run();
            case "manifest-sensitive-names":
                if (args.Length < 2) { PrintUsage(); return 2; }
                return RunManifestSensitiveNames(args[1]);
            case "secret-outputs":
                if (args.Length < 2) { PrintUsage(); return 2; }
                return RunSecretOutputs(args[1]);
            default:
                PrintUsage();
                return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  Tools.TerraformLint manifest-sensitive-names <manifests-dir>");
        Console.Error.WriteLine("  Tools.TerraformLint secret-outputs <stock-modules-root>");
        Console.Error.WriteLine("  Tools.TerraformLint --self-test");
    }

    private static int RunManifestSensitiveNames(string dir)
    {
        if (!TryResolveDirectoryArg(dir, out var d)) return 2;
        ManifestSensitiveNamesLint.LintDirectory(d, Console.Out);
        return 0;
    }

    private static int RunSecretOutputs(string dir)
    {
        if (!TryResolveDirectoryArg(dir, out var d)) return 2;
        return SecretOutputsLint.LintDirectory(d, Console.Error) > 0 ? 1 : 0;
    }

    // Reject CLI-supplied paths that contain `..` segments and reject paths
    // outside the current working directory's tree. The CI workflow always
    // passes a relative path inside the checkout (e.g. `stock-modules/`); the
    // guard fails closed for any unexpected input shape.
    private static bool TryResolveDirectoryArg(string raw, out DirectoryInfo resolved)
    {
        resolved = null!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            Console.Error.WriteLine("error: directory argument is empty");
            return false;
        }
        var segments = raw.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
        {
            Console.Error.WriteLine($"error: directory argument must not contain '..' segments: {raw}");
            return false;
        }
        var d = new DirectoryInfo(raw);
        if (!d.Exists)
        {
            Console.Error.WriteLine($"error: {raw} is not a directory");
            return false;
        }
        var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
        var resolvedFull = Path.GetFullPath(d.FullName);
        if (!resolvedFull.StartsWith(cwd, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"error: directory must be inside the current working directory tree: {raw}");
            return false;
        }
        resolved = d;
        return true;
    }
}

// ----- Manifest sensitive-name lint -----

public static class ManifestSensitiveNamesLint
{
    public static readonly string[] DefaultPatterns =
        { "password", "secret", "key", "token", "connection_string" };

    public static IReadOnlyList<string> LoadPatterns(DirectoryInfo manifestsDir)
    {
        var configFile = Path.Join(manifestsDir.FullName, ".sensitive-name-patterns");
        if (configFile.Contains(".."))
            throw new ArgumentException("manifestsDir must not contain '..' segments");
        if (!File.Exists(configFile))
            return DefaultPatterns;
        return File.ReadAllLines(configFile)
            .Select(raw => raw.Trim())
            .Where(trimmed => !string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
            .ToList();
    }

    public static int LintDirectory(DirectoryInfo dir, TextWriter outWriter)
    {
        var patterns = LoadPatterns(dir);
        if (patterns.Count == 0)
        {
            var configFile = Path.Join(dir.FullName, ".sensitive-name-patterns");
            outWriter.WriteLine(
                $"::warning file={configFile}::sensitive-name pattern file is empty after comment/blank-line stripping; lint produced no warnings.");
            return 0;
        }
        var warnings = 0;
        foreach (var yaml in dir.GetFiles("*.yaml", SearchOption.TopDirectoryOnly).OrderBy(f => f.Name))
        {
            foreach (var w in LintFile(yaml.FullName, patterns))
            {
                outWriter.WriteLine(w);
                warnings++;
            }
        }
        return warnings;
    }

    public static IEnumerable<string> LintFile(string path, IReadOnlyList<string> patterns)
    {
        if (path == null || path.Contains(".."))
            throw new ArgumentException("path must not contain '..' segments");
        if (patterns.Count == 0) yield break;
        var text = File.ReadAllText(path);
        var paramsBlock = FindParametersBlock(text);
        if (paramsBlock is null) yield break;

        var combined = new Regex(
            string.Join("|", patterns.Select(Regex.Escape)),
            RegexOptions.IgnoreCase);
        var paramName = new Regex(
            @"^[ \t]*-[ \t]*name:[ \t]*(?<name>[A-Za-z0-9_]+)[ \t]*$",
            RegexOptions.Multiline);
        var sensitiveTrue = new Regex(
            @"^[ \t]+sensitive:[ \t]*true[ \t]*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        var matches = paramName.Matches(paramsBlock);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var name = match.Groups["name"].Value;
            var bodyStart = match.Index;
            var bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : paramsBlock.Length;
            var body = paramsBlock.Substring(bodyStart, bodyEnd - bodyStart);
            if (!combined.IsMatch(name)) continue;
            if (sensitiveTrue.IsMatch(body)) continue;
            yield return
                $"::warning file={path}::Parameter '{name}' name matches a secret-shape pattern but does not declare sensitive: true. Either set sensitive: true or rename the parameter.";
        }
    }

    public static string? FindParametersBlock(string yaml)
    {
        var header = Regex.Match(yaml, @"^parameters:\s*$", RegexOptions.Multiline);
        if (!header.Success) return null;
        var afterHeader = header.Index + header.Length;
        var rest = yaml.Substring(afterHeader);
        var endMatch = Regex.Match(rest, @"^(?![ \t-])\S", RegexOptions.Multiline);
        return endMatch.Success ? rest.Substring(0, endMatch.Index) : rest;
    }
}

// ----- Secret-output check -----

public static class SecretOutputsLint
{
    private static readonly Regex SecretNamePattern =
        new(@"(token|pat|secret|password|key|connectionstring)", RegexOptions.IgnoreCase);

    private static readonly Regex OutputBlock =
        new(@"output\s+""([^""]+)""\s*\{([^}]*)\}", RegexOptions.Singleline);

    private static readonly Regex SensitiveFalse =
        new(@"sensitive\s*=\s*false", RegexOptions.IgnoreCase);

    private static readonly Regex SensitiveTrue =
        new(@"sensitive\s*=\s*true", RegexOptions.IgnoreCase);

    public static int LintDirectory(DirectoryInfo root, TextWriter errWriter)
    {
        var failures = 0;
        foreach (var file in root.GetFiles("outputs.tf", SearchOption.AllDirectories).OrderBy(f => f.FullName))
        {
            failures += LintFile(file.FullName, errWriter);
        }
        return failures;
    }

    public static int LintFile(string path, TextWriter errWriter)
    {
        if (path == null || path.Contains(".."))
            throw new ArgumentException("path must not contain '..' segments");
        var src = File.ReadAllText(path);
        var failures = 0;
        foreach (Match m in OutputBlock.Matches(src))
        {
            var name = m.Groups[1].Value;
            var body = m.Groups[2].Value;
            if (!SecretNamePattern.IsMatch(name)) continue;
            if (SensitiveFalse.IsMatch(body))
            {
                errWriter.WriteLine($"{path}: output \"{name}\" must not declare sensitive = false");
                failures++;
            }
            else if (!SensitiveTrue.IsMatch(body))
            {
                errWriter.WriteLine($"{path}: output \"{name}\" matches secret pattern; declare sensitive = true");
                failures++;
            }
        }
        return failures;
    }
}

// ----- Self-test -----

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            ManifestSensitiveNamesSelfTest();
            SecretOutputsSelfTest();
            Console.WriteLine("self-test passed");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                  || ex is IOException
                                  || ex is UnauthorizedAccessException
                                  || ex is ArgumentException)
        {
            Console.Error.WriteLine($"self-test failed: {ex.Message}");
            return 1;
        }
    }

    private static void ManifestSensitiveNamesSelfTest()
    {
        var temp = new DirectoryInfo(Path.Join(Path.GetTempPath(), Path.GetRandomFileName()));
        temp.Create();
        try
        {
            File.WriteAllText(
                Path.Join(temp.FullName, "safe-1.0.0.yaml"),
                "name: safe\nversion: 1.0.0\nparameters:\n  - name: tags\n    type: String\n    required: false\noutputs: []\n");
            File.WriteAllText(
                Path.Join(temp.FullName, "risky-1.0.0.yaml"),
                "name: risky\nversion: 1.0.0\nparameters:\n  - name: database_password\n    type: String\n    required: true\noutputs: []\n");

            var warnings = CollectManifestWarnings(temp, ManifestSensitiveNamesLint.DefaultPatterns);
            AssertCount("manifest default patterns: expected one warning", 1, warnings.Count);
            if (!warnings[0].Contains("database_password"))
                throw new InvalidOperationException($"warning should name offending param: {warnings[0]}");

            File.WriteAllText(
                Path.Join(temp.FullName, ".sensitive-name-patterns"),
                "password\nsecret\nkey\ntoken\nconnection_string\ntags\n");
            var custom = ManifestSensitiveNamesLint.LoadPatterns(temp);
            warnings = CollectManifestWarnings(temp, custom);
            AssertCount("manifest custom patterns: expected two warnings", 2, warnings.Count);
            if (!warnings.Any(w => w.Contains("'tags'")))
                throw new InvalidOperationException($"warns should include 'tags': {string.Join(" | ", warnings)}");

            File.Delete(Path.Join(temp.FullName, ".sensitive-name-patterns"));
            warnings = CollectManifestWarnings(temp, ManifestSensitiveNamesLint.DefaultPatterns);
            AssertCount("manifest reverted: expected one warning", 1, warnings.Count);

            File.WriteAllText(
                Path.Join(temp.FullName, "risky-1.0.0.yaml"),
                "name: risky\nversion: 1.0.0\nparameters:\n  - name: database_password\n    type: String\n    required: true\n    sensitive: true\noutputs: []\n");
            warnings = CollectManifestWarnings(temp, ManifestSensitiveNamesLint.DefaultPatterns);
            AssertCount("manifest sensitive: true should suppress warning", 0, warnings.Count);
        }
        finally
        {
            try { Directory.Delete(temp.FullName, recursive: true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"self-test: temp-dir cleanup failed for '{temp.FullName}': {ex.Message}");
            }
        }
    }

    private static void SecretOutputsSelfTest()
    {
        var temp = new DirectoryInfo(Path.Join(Path.GetTempPath(), Path.GetRandomFileName()));
        temp.Create();
        try
        {
            // Module 1: clean — secret-named output with sensitive = true.
            var mod1 = Directory.CreateDirectory(Path.Join(temp.FullName, "module-clean"));
            File.WriteAllText(Path.Join(mod1.FullName, "outputs.tf"),
                "output \"connection_string\" {\n  value     = \"foo\"\n  sensitive = true\n}\n");

            // Module 2: secret-named output with explicit sensitive = false (must fail).
            var mod2 = Directory.CreateDirectory(Path.Join(temp.FullName, "module-explicit-false"));
            File.WriteAllText(Path.Join(mod2.FullName, "outputs.tf"),
                "output \"api_key\" {\n  value     = \"bar\"\n  sensitive = false\n}\n");

            // Module 3: secret-named output missing sensitive entirely (must fail).
            var mod3 = Directory.CreateDirectory(Path.Join(temp.FullName, "module-missing"));
            File.WriteAllText(Path.Join(mod3.FullName, "outputs.tf"),
                "output \"admin_password\" {\n  value = \"baz\"\n}\n");

            // Module 4: non-secret name — should not be flagged regardless of sensitive flag.
            var mod4 = Directory.CreateDirectory(Path.Join(temp.FullName, "module-non-secret"));
            File.WriteAllText(Path.Join(mod4.FullName, "outputs.tf"),
                "output \"resource_id\" {\n  value = \"qux\"\n}\n");

            using var sw = new StringWriter();
            var failures = SecretOutputsLint.LintDirectory(temp, sw);
            AssertCount("secret-outputs: expected exactly two failures", 2, failures);
            var output = sw.ToString();
            if (!output.Contains("api_key") || !output.Contains("admin_password"))
                throw new InvalidOperationException($"failures should name api_key and admin_password: {output}");
            if (output.Contains("connection_string") || output.Contains("resource_id"))
                throw new InvalidOperationException($"clean / non-secret modules must not be flagged: {output}");
        }
        finally
        {
            try { Directory.Delete(temp.FullName, recursive: true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"self-test: temp-dir cleanup failed for '{temp.FullName}': {ex.Message}");
            }
        }
    }

    private static List<string> CollectManifestWarnings(DirectoryInfo dir, IReadOnlyList<string> patterns)
    {
        var warnings = new List<string>();
        foreach (var yaml in dir.GetFiles("*.yaml", SearchOption.TopDirectoryOnly).OrderBy(f => f.Name))
            warnings.AddRange(ManifestSensitiveNamesLint.LintFile(yaml.FullName, patterns));
        return warnings;
    }

    private static void AssertCount(string label, int expected, int actual)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
