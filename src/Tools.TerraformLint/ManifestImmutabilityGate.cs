using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tools.TerraformLint;

// Fail-closed published catalog identity/payload retention gate.
//
// Compares every release manifest reachable from merge-base(BASE, HEAD) with
// the manifests at HEAD by release *identity* (name@version, case-insensitive,
// "v" prefix ignored) rather than by path or diff status, so renames, deletes
// and delete-then-re-add cannot change a published payload. Only the
// deprecation metadata of a published release may change, and only one way.
public sealed class ManifestImmutabilityGate
{
    public const string ManifestsRoot = "stock-modules-manifests/";

    private static readonly Regex NamePattern = new("^[a-zA-Z0-9._-]{1,256}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(@"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex CommitShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex RefPattern = new("^[a-zA-Z0-9_./-]{1,256}$", RegexOptions.CultureInvariant);
    private static readonly Regex ParameterNamePattern = new("^[a-zA-Z0-9_]+$", RegexOptions.CultureInvariant);
    private static readonly string[] ParameterTypes = { "String", "Number", "Bool" };

    private readonly Func<string[], byte[]> git;

    public ManifestImmutabilityGate(Func<string[], byte[]>? git = null)
    {
        this.git = git ?? RunGit;
    }

    public static int Run(string baseSha, string headSha, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var gate = new ManifestImmutabilityGate();
            var mergeBase = Encoding.UTF8.GetString(gate.git(new[] { "merge-base", baseSha, headSha })).Trim();
            gate.Check(gate.Snapshot(mergeBase), gate.Snapshot(headSha));
        }
        catch (Exception ex) when (ex is GateViolationException
                                  || ex is YamlException
                                  || ex is InvalidOperationException
                                  || ex is IOException
                                  || ex is FormatException)
        {
            stderr.WriteLine($"manifest-immutability: {ex.Message}; failing closed.");
            return 1;
        }
        stdout.WriteLine("manifest-immutability: all published identities retained and payloads unchanged.");
        return 0;
    }

    // Reads the manifests from the Git tree at the revision (never from the
    // working copy) and inspects tree modes so a symlink cannot smuggle content.
    public Dictionary<string, YamlMappingNode> Snapshot(string revision)
    {
        var result = new Dictionary<string, YamlMappingNode>(StringComparer.Ordinal);
        var listing = git(new[] { "ls-tree", "-rz", revision, "--", ManifestsRoot });
        foreach (var entry in Encoding.UTF8.GetString(listing).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) throw new GateViolationException($"unexpected ls-tree entry: {entry}");
            var path = entry.Substring(tab + 1);
            if (!path.EndsWith(".yaml", StringComparison.Ordinal)) continue;
            var metadata = entry.Substring(0, tab).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length != 3) throw new GateViolationException($"unexpected ls-tree entry: {entry}");
            var (mode, kind, oid) = (metadata[0], metadata[1], metadata[2]);
            if ((mode != "100644" && mode != "100755") || kind != "blob")
                throw new GateViolationException($"{path}: release manifest must be a regular file");
            if (path.Count(c => c == '/') != 1)
                throw new GateViolationException($"{path}: release manifests must be in the catalog root");
            result[path] = ParseManifest(path, Encoding.UTF8.GetString(git(new[] { "cat-file", "blob", oid })));
        }
        return result;
    }

    public static YamlMappingNode ParseManifest(string path, string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            // YamlDotNet rejects duplicate mapping keys itself; surface it as a
            // gate violation so the failure names the manifest.
            throw new GateViolationException($"{path}: {ex.Message}");
        }
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            throw new GateViolationException($"{path}: expected a YAML mapping");
        return mapping;
    }

    public void Check(IReadOnlyDictionary<string, YamlMappingNode> baseFiles, IReadOnlyDictionary<string, YamlMappingNode> headFiles)
    {
        var baseIdentities = Identities(baseFiles, validatePins: false);
        var headIdentities = Identities(headFiles, validatePins: true);
        foreach (var (identity, (oldPath, old)) in baseIdentities)
        {
            if (!headIdentities.TryGetValue(identity, out var head))
                throw new GateViolationException($"{oldPath}: published releases must be retained; deprecate instead of deleting");
            var (newPath, updated) = head;
            if (!string.Equals(Payload(old), Payload(updated), StringComparison.Ordinal))
                throw new GateViolationException($"{newPath}: immutable release payload/identity changed; publish a new version");
            var wasDeprecated = Deprecated(oldPath, old);
            var isDeprecated = Deprecated(newPath, updated);
            if (wasDeprecated && !isDeprecated)
                throw new GateViolationException($"{newPath}: deprecation is one-way; publish a new release instead of reactivating");
            if (!isDeprecated && !string.Equals(ScalarOrNull(old, "deprecation_reason"), ScalarOrNull(updated, "deprecation_reason"), StringComparison.Ordinal))
                throw new GateViolationException($"{newPath}: deprecation_reason can only change on a deprecated release");
        }
    }

    public static Dictionary<string, (string Path, YamlMappingNode Manifest)> Identities(
        IReadOnlyDictionary<string, YamlMappingNode> files, bool validatePins)
    {
        var result = new Dictionary<string, (string, YamlMappingNode)>(StringComparer.Ordinal);
        foreach (var (path, manifest) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var name = ScalarOrNull(manifest, "name");
            if (name is null || !NamePattern.IsMatch(name))
                throw new GateViolationException($"{path}: invalid name");
            var version = ScalarOrNull(manifest, "version");
            if (version is null || version.Length > 64 || !VersionPattern.IsMatch(version))
                throw new GateViolationException($"{path}: only stable major.minor.patch releases are supported");
            // "v2.1.0" and "2.1.0" resolve to the same catalog release, so they
            // are the same identity; otherwise a v-alias could shadow a release.
            var key = name.ToUpperInvariant() + "@" + version.TrimStart('v');
            if (result.TryGetValue(key, out var existing))
                throw new GateViolationException($"{path}: duplicate release {name}@{version} (also {existing.Item1})");
            if (validatePins) ValidatePins(path, manifest);
            if (Deprecated(path, manifest) && string.IsNullOrWhiteSpace(ScalarOrNull(manifest, "deprecation_reason")))
                throw new GateViolationException($"{path}: deprecated releases require a deprecation_reason");
            result[key] = (path, manifest);
        }
        return result;
    }

    private static void ValidatePins(string path, YamlMappingNode manifest)
    {
        if (Child(manifest, "source") is not YamlMappingNode source || ScalarOrNull(source, "kind") != "git")
            throw new GateViolationException($"{path}: source.kind must be git");
        var sha = ScalarOrNull(source, "commit_sha");
        if (sha is null || !CommitShaPattern.IsMatch(sha))
            throw new GateViolationException($"{path}: source.commit_sha must be a full Git commit SHA");
        var reference = ScalarOrNull(source, "ref");
        if (reference is null || !RefPattern.IsMatch(reference))
            throw new GateViolationException($"{path}: source.ref must identify a release tag");
        var locator = ScalarOrNull(source, "locator");
        if (locator is null || !Uri.TryCreate(locator, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
            throw new GateViolationException($"{path}: source.locator must be an absolute HTTPS URL");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var parametersNode = Child(manifest, "parameters");
        if (parametersNode is not null && parametersNode is not YamlSequenceNode)
            throw new GateViolationException($"{path}: parameters must be a list");
        foreach (var parameterNode in (parametersNode as YamlSequenceNode)?.Children ?? Enumerable.Empty<YamlNode>())
        {
            if (parameterNode is not YamlMappingNode parameter
                || ScalarOrNull(parameter, "name") is not string parameterName
                || !ParameterNamePattern.IsMatch(parameterName))
                throw new GateViolationException($"{path}: parameter names must contain only [a-zA-Z0-9_]");
            if (!names.Add(parameterName))
                throw new GateViolationException($"{path}: duplicate parameter '{parameterName}'");
            if (!ParameterTypes.Contains(ScalarOrNull(parameter, "type"), StringComparer.Ordinal))
                throw new GateViolationException($"{path}: parameter '{parameterName}' has unsupported type");
            var integer = Child(parameter, "integer");
            if (integer is not null && (!TryBool(integer, out var isInteger) || (isInteger && ScalarOrNull(parameter, "type") != "Number")))
                throw new GateViolationException($"{path}: integer constraints require Number parameters");
        }

        var conflictsNode = Child(manifest, "parameter_conflicts");
        if (conflictsNode is not null && conflictsNode is not YamlSequenceNode)
            throw new GateViolationException($"{path}: parameter_conflicts must be a list");
        foreach (var conflictNode in (conflictsNode as YamlSequenceNode)?.Children ?? Enumerable.Empty<YamlNode>())
        {
            if (conflictNode is not YamlMappingNode conflict
                || !names.Contains(ScalarOrNull(conflict, "parameter") ?? string.Empty)
                || !names.Contains(ScalarOrNull(conflict, "when_parameter") ?? string.Empty))
                throw new GateViolationException($"{path}: parameter_conflicts must reference declared parameters");
            if (ScalarOrNull(conflict, "value") is null || ScalarOrNull(conflict, "when_value") is null
                || string.IsNullOrWhiteSpace(ScalarOrNull(conflict, "message")))
                throw new GateViolationException($"{path}: parameter_conflicts require string values and a nonempty message");
        }
    }

    private static bool Deprecated(string path, YamlMappingNode manifest)
    {
        var node = Child(manifest, "deprecated");
        if (node is null) return false;
        if (!TryBool(node, out var value))
            throw new GateViolationException($"{path}: deprecated must be a YAML boolean");
        return value;
    }

    // Canonical form of everything except the mutable deprecation metadata.
    // Scalars keep their YAML style and resolved type so "1", 1 and true can
    // never be swapped behind equal-looking values.
    public static string Payload(YamlMappingNode manifest)
    {
        var builder = new StringBuilder();
        WriteCanonical(builder, manifest, skipTopLevel: new HashSet<string>(StringComparer.Ordinal) { "deprecated", "deprecation_reason" });
        return builder.ToString();
    }

    private static void WriteCanonical(StringBuilder builder, YamlNode node, HashSet<string>? skipTopLevel = null)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                builder.Append('{');
                var first = true;
                foreach (var (key, value) in mapping.Children
                             .Select(c => (Key: ((YamlScalarNode)c.Key).Value ?? string.Empty, c.Value))
                             .Where(c => skipTopLevel is null || !skipTopLevel.Contains(c.Key))
                             .OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(',');
                    first = false;
                    AppendQuoted(builder, key);
                    builder.Append(':');
                    WriteCanonical(builder, value);
                }
                builder.Append('}');
                break;
            case YamlSequenceNode sequence:
                builder.Append('[');
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    WriteCanonical(builder, sequence.Children[i]);
                }
                builder.Append(']');
                break;
            case YamlScalarNode scalar:
                AppendQuoted(builder, ScalarKind(scalar) + ":" + (scalar.Value ?? string.Empty));
                break;
            default:
                throw new GateViolationException($"unsupported YAML node: {node.NodeType}");
        }
    }

    private static string ScalarKind(YamlScalarNode scalar)
    {
        if (scalar.Style != ScalarStyle.Plain) return "str";
        var value = scalar.Value ?? string.Empty;
        if (IsNull(value)) return "null";
        if (IsBool(value)) return "bool";
        if (BigInteger.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) return "int";
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return "float";
        return "str";
    }

    private static bool IsNull(string value) => value is "" or "~" or "null" or "Null" or "NULL";
    private static bool IsBool(string value) => value is "true" or "True" or "TRUE" or "false" or "False" or "FALSE";

    private static bool TryBool(YamlNode node, out bool value)
    {
        value = false;
        if (node is not YamlScalarNode scalar || scalar.Style != ScalarStyle.Plain || !IsBool(scalar.Value ?? string.Empty))
            return false;
        value = (scalar.Value ?? string.Empty).StartsWith('t') || (scalar.Value ?? string.Empty).StartsWith('T');
        return true;
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            if (c == '"' || c == '\\') builder.Append('\\');
            builder.Append(c);
        }
        builder.Append('"');
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key)
    {
        foreach (var (childKey, value) in mapping.Children)
        {
            if (childKey is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
                return value;
        }
        return null;
    }

    private static string? ScalarOrNull(YamlMappingNode mapping, string key)
    {
        return Child(mapping, key) is YamlScalarNode scalar ? scalar.Value : null;
    }

    private static byte[] RunGit(string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git could not be started");
        using var stdout = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(stdout);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr.Result.Trim()}");
        return stdout.ToArray();
    }
}

public sealed class GateViolationException : Exception
{
    public GateViolationException(string message) : base(message) { }
}

internal static class ManifestImmutabilitySelfTest
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static void Run()
    {
        var baseline = Files(("stock-modules-manifests/module.yaml", Manifest()));
        var gate = new ManifestImmutabilityGate();

        gate.Check(baseline, Files(
            ("stock-modules-manifests/renamed.yaml", Manifest()),
            ("stock-modules-manifests/new.yaml", Manifest("1.1.0"))));

        foreach (var name in new[] { "module", "MODULE" })
            Expect("duplicate release", () => gate.Check(baseline, Files(
                ("stock-modules-manifests/module.yaml", Manifest()),
                ("stock-modules-manifests/aaa.yaml", Manifest(name: name)))));

        Expect("duplicate release", () => gate.Check(baseline, Files(
            ("stock-modules-manifests/module.yaml", Manifest()),
            ("stock-modules-manifests/module-v1.yaml", Manifest("v1.0.0", sha: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")))));

        Expect("retained", () => gate.Check(Files(("old.yaml", Manifest())), Files()));
        Expect("retained", () => gate.Check(Files(("old.yaml", Manifest(deprecated: true, reason: "superseded"))), Files()));

        Expect("immutable", () => gate.Check(baseline, Files(("readded.yaml", Manifest(extra: "parameters:\n  - name: injected\n    type: String\n")))));
        Expect("immutable", () => gate.Check(baseline, Files(("readded.yaml", Manifest(extra: "description: changed\n")))));
        Expect("retained", () => gate.Check(baseline, Files(("readded.yaml", Manifest(name: "different")))));
        Expect("retained", () => gate.Check(baseline, Files(("readded.yaml", Manifest("2.0.0")))));
        Expect("immutable", () => gate.Check(baseline, Files(("renamed.yaml", Manifest(sha: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")))));

        var deprecated = Manifest(deprecated: true, reason: "use 2.0.0");
        gate.Check(baseline, Files(("renamed.yaml", deprecated)));
        gate.Check(Files(("old.yaml", deprecated)), Files(("new.yaml", Manifest(deprecated: true, reason: "clarified successor"))));
        Expect("one-way", () => gate.Check(Files(("old.yaml", deprecated)), baseline));
        Expect("deprecation_reason can only change", () => gate.Check(baseline, Files(("new.yaml", Manifest(reason: "changed")))));

        foreach (var sha in new[] { "", "abc123", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz" })
            Expect("commit_sha", () => gate.Check(Files(), Files(("new.yaml", Manifest(sha: sha)))));
        Expect("deprecation_reason", () => gate.Check(baseline, Files(("new.yaml", Manifest(deprecated: true, reason: "")))));
        Expect("stable", () => gate.Check(Files(), Files(("new.yaml", Manifest("2.0.0-rc.1")))));

        Expect("Duplicate key", () => ManifestImmutabilityGate.ParseManifest("dup.yaml", "name: one\nname: two\n"));

        Expect("immutable", () => gate.Check(
            Files(("old.yaml", Manifest(extra: "parameters:\n  - name: input\n    type: String\n    default: true\n"))),
            Files(("new.yaml", Manifest(extra: "parameters:\n  - name: input\n    type: String\n    default: 1\n")))));
        Expect("immutable", () => gate.Check(
            Files(("old.yaml", Manifest(extra: "parameters:\n  - name: input\n    type: String\n    default: \"1\"\n"))),
            Files(("new.yaml", Manifest(extra: "parameters:\n  - name: input\n    type: String\n    default: 1\n")))));

        var symlink = Encoding.UTF8.GetBytes("120000 blob " + Sha + "\tstock-modules-manifests/release.yaml\0");
        Expect("regular file", () => new ManifestImmutabilityGate(_ => symlink).Snapshot("head"));
        var calls = 0;
        var snapshot = new ManifestImmutabilityGate(args =>
            calls++ == 0
                ? Encoding.UTF8.GetBytes("100644 blob " + Sha + "\tstock-modules-manifests/release.yaml\0")
                : Encoding.UTF8.GetBytes(Manifest())).Snapshot("head");
        if (snapshot.Count != 1 || !snapshot.ContainsKey("stock-modules-manifests/release.yaml"))
            throw new InvalidOperationException("snapshot should load the real manifest from the blob");

        var errors = new StringWriter();
        if (ManifestImmutabilityGate.Run("no-such-base-ref", "no-such-head-ref", TextWriter.Null, errors) != 1
            || !errors.ToString().Contains("failing closed", StringComparison.Ordinal))
            throw new InvalidOperationException("git failures must fail closed");
    }

    private static void Expect(string messageFragment, Action action)
    {
        try
        {
            action();
        }
        catch (GateViolationException ex) when (ex.Message.Contains(messageFragment, StringComparison.Ordinal))
        {
            return;
        }
        catch (GateViolationException ex)
        {
            throw new InvalidOperationException($"expected violation containing '{messageFragment}' but got '{ex.Message}'");
        }
        throw new InvalidOperationException($"expected violation containing '{messageFragment}' but the gate passed");
    }

    private static Dictionary<string, YamlMappingNode> Files(params (string Path, string Yaml)[] files)
    {
        return files.ToDictionary(f => f.Path, f => ManifestImmutabilityGate.ParseManifest(f.Path, f.Yaml), StringComparer.Ordinal);
    }

    private static string Manifest(string version = "1.0.0", string name = "module", string sha = Sha,
        bool deprecated = false, string reason = "", string extra = "parameters: []\n")
    {
        return $"name: {name}\nversion: {version}\nsource:\n  kind: git\n  locator: https://example.com/modules.git\n"
             + $"  ref: module/v{version}\n  commit_sha: \"{sha}\"\n{extra}deprecated: {(deprecated ? "true" : "false")}\ndeprecation_reason: \"{reason}\"\n";
    }
}
