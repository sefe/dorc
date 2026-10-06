using System.Runtime.Versioning;

namespace Dorc.Installer.Tests;

/// <summary>
/// The gate on the installer decomposition: the four packages together must
/// install exactly what the single Setup.Dorc.msi installed.
///
/// The two self-tests come first and exist because of how this failed before.
/// An earlier version compared empty sets — every table read, every row
/// dropped — and an empty set compares equal to anything, so it reported two
/// entirely unrelated packages as identical. A comparison that cannot fail is
/// not a gate, so one test proves it reports nothing when there is nothing to
/// report, and another proves it still reports something when there is.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class InstallerPackageEquivalenceTests
{
    /// <summary>Set by CI to the folder holding the pre-split Setup.Dorc.msi.</summary>
    private const string BaselineFolderVariable = "MSI_BASELINE_PATH";

    private const string BaselineFileName = "Setup.Dorc.msi";
    private const string UnrelatedPackage = "Setup.Acceptance.msi";

    private static readonly string[] ProductPackages =
    [
        "Setup.Dorc.Api.msi",
        "Setup.Dorc.Web.msi",
        "Setup.Dorc.Monitors.msi",
        "Setup.Dorc.Cli.msi"
    ];

    [TestMethod]
    public void EveryPackageComparedAgainstItself_ReportsNoDrift()
    {
        foreach (var package in BuiltPackages(ProductPackages))
        {
            var payload = InstallerPayload.Read(package);

            Assert.AreNotEqual(0, payload.Files.Count,
                $"{Path.GetFileName(package)} yielded no files at all. An empty set compares equal to anything, "
                + "so this is a fault in the comparison, not a package with no payload.");

            var drift = Drift(payload, [payload]);
            Assert.AreEqual(0, drift.Count,
                $"{Path.GetFileName(package)} differs from itself: {string.Join("; ", drift.Take(5))}");
        }
    }

    [TestMethod]
    public void UnrelatedPackages_ReportDrift()
    {
        var product = BuiltPackages(ProductPackages).FirstOrDefault();
        var unrelated = BuiltPackages([UnrelatedPackage]).FirstOrDefault();
        if (product is null || unrelated is null)
        {
            Assert.Inconclusive($"Needs a product package and {UnrelatedPackage}; the build produced only "
                                + string.Join(", ", BuiltPackages(ProductPackages.Append(UnrelatedPackage).ToArray())
                                    .Select(Path.GetFileName)));
        }

        var drift = Drift(InstallerPayload.Read(product!), [InstallerPayload.Read(unrelated!)]);
        Assert.AreNotEqual(0, drift.Count,
            $"{Path.GetFileName(product!)} and {Path.GetFileName(unrelated!)} share no payload, so a comparison "
            + "reporting them identical is broken and would pass anything.");
    }

    [TestMethod]
    public void FourPackagesTogether_InstallWhatTheBaselineInstalled()
    {
        var baselineFolder = Environment.GetEnvironmentVariable(BaselineFolderVariable);
        if (string.IsNullOrWhiteSpace(baselineFolder))
        {
            Assert.Inconclusive($"{BaselineFolderVariable} is not set, so there is nothing to compare against. "
                                + "Point it at the folder holding the pre-split " + BaselineFileName + ".");
        }

        var baselinePath = Directory.Exists(baselineFolder)
            ? Directory.EnumerateFiles(baselineFolder!, BaselineFileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        Assert.IsNotNull(baselinePath,
            $"{BaselineFolderVariable} is set to '{baselineFolder}' but no {BaselineFileName} was found under it. "
            + "A configured baseline that cannot be read is a failure, not a reason to skip.");

        var packages = BuiltPackages(ProductPackages);
        CollectionAssert.AreEquivalent(ProductPackages,
            packages.Select(Path.GetFileName).ToArray(),
            "The build did not produce all four packages, so their union cannot be compared to the baseline.");

        var drift = Drift(InstallerPayload.Read(baselinePath!), packages.Select(InstallerPayload.Read).ToArray());
        var missing = drift.Where(d => d.StartsWith("MISSING", StringComparison.Ordinal)).ToList();
        var extra = drift.Where(d => d.StartsWith("EXTRA", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(0, drift.Count,
            $"The packages no longer install what the baseline installed: {missing.Count} missing, "
            + $"{extra.Count} extra." + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Take(30).Concat(extra.Take(30))));
    }

    /// <summary>
    /// Everything in the baseline that no package installs, and everything the
    /// packages install that the baseline did not — across all three sets.
    /// </summary>
    private static List<string> Drift(InstallerPayload baseline, IReadOnlyCollection<InstallerPayload> packages)
    {
        var drift = new List<string>();
        Compare("file", baseline.Files, packages.SelectMany(p => p.Files));
        Compare("registry value", baseline.RegistryValues, packages.SelectMany(p => p.RegistryValues));
        Compare("certificate", baseline.Certificates, packages.SelectMany(p => p.Certificates));
        return drift;

        void Compare(string label, IReadOnlySet<string> expected, IEnumerable<string> actual)
        {
            var union = actual.ToHashSet(StringComparer.Ordinal);
            drift.AddRange(expected.Except(union).OrderBy(e => e, StringComparer.Ordinal)
                .Select(e => $"MISSING {label}: {e}"));
            drift.AddRange(union.Except(expected).OrderBy(e => e, StringComparer.Ordinal)
                .Select(e => $"EXTRA {label}: {e}"));
        }
    }

    /// <summary>
    /// The named packages that this build actually produced, newest first when
    /// a name matches more than once.
    /// </summary>
    private static string[] BuiltPackages(params string[] names)
    {
        var root = RepositoryRoot();
        return names
            .Select(name => Directory
                .EnumerateFiles(Path.Combine(root, "src"), name, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Setup.Dorc.Api")))
            directory = directory.Parent;

        Assert.IsNotNull(directory,
            "Could not locate the repository root (a directory containing src/Setup.Dorc.Api) above "
            + AppContext.BaseDirectory);
        return directory!.FullName;
    }
}
