using System.Runtime.Versioning;

namespace Dorc.Installer.Tests;

/// <summary>
/// What a package actually installs, in a form two packages can be compared
/// on. Three sets, because the file table alone cannot see two of the
/// resources the decomposition is most likely to lose:
/// <list type="bullet">
/// <item><description><b>Files</b> — target directory, file name and version.
/// File Ids are deliberately excluded: re-harvesting into new wixproj files
/// regenerates them, so comparing Ids reports near-total churn and says
/// nothing.</description></item>
/// <item><description><b>Registry</b> — <c>RegistryEntries</c> carries no
/// file, so nothing in the file comparison would notice it disappearing.</description></item>
/// <item><description><b>Certificates</b> — <c>iis:Certificate</c> compiles to
/// a custom action rather than a reference-counted resource, which makes it
/// the hardest resource to keep correct across a split.</description></item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed record InstallerPayload(
    IReadOnlySet<string> Files,
    IReadOnlySet<string> RegistryValues,
    IReadOnlySet<string> Certificates)
{
    public static InstallerPayload Read(string packagePath)
    {
        using var db = new InstallerDatabase(packagePath);

        var files = db.Read("File", "Component_", "FileName", "Version");
        var components = db.Read("Component", "Component", "Directory_");
        var directories = db.Read("Directory", "Directory", "Directory_Parent", "DefaultDir");

        var componentDirectory = components.ToDictionary(c => c[0], c => c[1], StringComparer.Ordinal);
        var directoryPaths = ResolveDirectoryPaths(directories);

        var fileSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var directory = componentDirectory.TryGetValue(file[0], out var directoryId)
                            && directoryPaths.TryGetValue(directoryId, out var path)
                ? path
                : "<unresolved>";
            fileSet.Add($"{directory}/{LongName(file[1])}|{file[2]}");
        }

        var registry = db.Read("Registry", "Root", "Key", "Name", "Value")
            .Select(r => $@"{r[0]}\{r[1]}\{r[2]}={r[3]}")
            .ToHashSet(StringComparer.Ordinal);

        var certificates = db.Read("Certificate", "Name", "StoreLocation", "StoreName")
            .Select(c => $"{c[0]}|{c[1]}|{c[2]}")
            .ToHashSet(StringComparer.Ordinal);

        return new InstallerPayload(fileSet, registry, certificates);
    }

    /// <summary>
    /// Full target path per directory, by walking Directory_Parent to the root.
    /// Iterative with a seen-set, so a cyclic Directory table cannot hang it.
    /// </summary>
    private static Dictionary<string, string> ResolveDirectoryPaths(IReadOnlyList<string[]> directories)
    {
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var nameOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            if (directory[0].Length == 0) continue;
            parentOf[directory[0]] = directory[1];
            var name = LongName(directory[2]);
            // '.' means "same as parent" and contributes no path segment.
            nameOf[directory[0]] = name == "." ? string.Empty : name;
        }

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in parentOf.Keys)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = id; current.Length > 0 && parentOf.ContainsKey(current) && seen.Add(current);)
            {
                chain.Add(current);
                current = parentOf[current];
            }

            var segments = Enumerable.Reverse(chain)
                .Select(step => nameOf[step])
                .Where(segment => segment.Length > 0);
            resolved[id] = string.Join('/', segments);
        }
        return resolved;
    }

    /// <summary>
    /// MSI stores short and long names as "SHORT|Long". The long name is what
    /// lands on disk, and what a human recognises.
    /// </summary>
    private static string LongName(string value)
    {
        var separator = value.IndexOf('|');
        return separator < 0 ? value : value[(separator + 1)..];
    }
}
