using System.Text.RegularExpressions;

namespace Dorc.Kafka.Client.Tests.Configuration;

/// <summary>
/// Drift guard for the Kafka installer surface. The Kafka settings flow
/// through four hand-maintained layers that nothing previously tied
/// together:
/// <list type="number">
/// <item><description>WiX JsonFile writes (<c>$.Kafka.*</c> ElementPaths) in the
/// Prod / NonProd Monitor .wxs files and the API .wxs;</description></item>
/// <item><description>MSI property defaults in <c>Install.Orchestrator.bat</c>;</description></item>
/// <item><description>MSIParameter → DeployProperty mappings in
/// <c>Setup.Dorc.msi.json</c>;</description></item>
/// <item><description>DeployProperty seeds in
/// <c>install-scripts/DeploySettings.template.json</c>.</description></item>
/// </list>
/// A property added to one layer but forgotten in another silently installs
/// an empty/unwritten setting. These tests fail the build with an actionable
/// message instead.
/// </summary>
[TestClass]
public class InstallerKafkaConfigConsistencyTests
{
    private static string RepoRoot()
    {
        // Same walk-up pattern as AppSettingsTemplateShapeTests' linked
        // templates, but anchored on the repo layout itself: ascend from the
        // test bin directory until the directory containing src/Setup.Dorc
        // exists.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "src", "Setup.Dorc")))
            dir = dir.Parent!;
        Assert.IsNotNull(dir, "Could not locate the repo root (a directory containing src/Setup.Dorc) above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var path = Path.Join(new[] { RepoRoot() }.Concat(segments).ToArray());
        Assert.IsTrue(File.Exists(path), $"Expected installer file not found: {path}");
        return File.ReadAllText(path);
    }

    private static string ProdWxs => ReadRepoFile("src", "Setup.Dorc", "Monitors", "Prod", "ProdActionService.wxs");
    private static string NonProdWxs => ReadRepoFile("src", "Setup.Dorc", "Monitors", "NonProd", "NonProdActionService.wxs");
    private static string RequestApiWxs => ReadRepoFile("src", "Setup.Dorc", "Web", "RequestApi", "RequestApi.wxs");
    private static string OrchestratorBat => ReadRepoFile("src", "Setup.Dorc", "Install.Orchestrator.bat");
    private static string MsiJson => ReadRepoFile("src", "Setup.Dorc", "Setup.Dorc.msi.json");
    private static string DeploySettingsTemplate => ReadRepoFile("src", "install-scripts", "DeploySettings.template.json");

    /// <summary>All <c>$.Kafka.*</c> JsonFile ElementPaths in a .wxs file.</summary>
    private static ISet<string> KafkaElementPaths(string wxs)
        => Regex.Matches(wxs, @"ElementPath=""(\$\.Kafka\.[^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>All <c>[KAFKA.*]</c> MSI properties referenced in a .wxs file.</summary>
    private static ISet<string> KafkaMsiProperties(string wxs)
        => Regex.Matches(wxs, @"\[(KAFKA\.[A-Z0-9.]+)\]")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>KAFKA.* properties given a default in Install.Orchestrator.bat.</summary>
    private static ISet<string> BatDefaults()
        => Regex.Matches(OrchestratorBat, @"^(KAFKA\.[A-Z0-9.]+)=", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>KAFKA.* → KAFKA_* mappings in Setup.Dorc.msi.json.</summary>
    private static IDictionary<string, string> MsiParameterMappings()
        => Regex.Matches(MsiJson,
                @"""MSIParameter"":\s*""(KAFKA\.[A-Z0-9.]+)"",\s*""DeployProperty"":\s*""(KAFKA_[A-Z0-9_]+)""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

    /// <summary>KAFKA_* deploy properties seeded in DeploySettings.template.json.</summary>
    private static ISet<string> TemplateDeployProperties()
        => Regex.Matches(DeploySettingsTemplate, @"""Name"":\s*""(KAFKA_[A-Z0-9_]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [TestMethod]
    public void ProdAndNonProdMonitorWxs_WriteTheSameKafkaElementPaths()
    {
        var prod = KafkaElementPaths(ProdWxs);
        var nonProd = KafkaElementPaths(NonProdWxs);

        Assert.IsTrue(prod.Count > 0, "No $.Kafka.* ElementPaths found in ProdActionService.wxs — the extraction regex or the file layout changed.");

        var missingFromNonProd = prod.Except(nonProd).OrderBy(p => p).ToList();
        var missingFromProd = nonProd.Except(prod).OrderBy(p => p).ToList();

        Assert.AreEqual(0, missingFromNonProd.Count,
            "Kafka ElementPath(s) written by ProdActionService.wxs but missing from NonProdActionService.wxs "
            + "(per-tier Values may differ, the PATH set must not): "
            + string.Join(", ", missingFromNonProd));
        Assert.AreEqual(0, missingFromProd.Count,
            "Kafka ElementPath(s) written by NonProdActionService.wxs but missing from ProdActionService.wxs: "
            + string.Join(", ", missingFromProd));
    }

    [TestMethod]
    public void EveryKafkaMsiPropertyReferencedInWxs_HasADefaultInOrchestratorBat()
    {
        var batDefaults = BatDefaults();
        Assert.IsTrue(batDefaults.Count > 0, "No KAFKA.* defaults found in Install.Orchestrator.bat — the extraction regex or the file layout changed.");

        foreach (var (file, wxs) in WxsFiles())
        {
            var missing = KafkaMsiProperties(wxs).Except(batDefaults).OrderBy(p => p).ToList();
            Assert.AreEqual(0, missing.Count,
                $"MSI propert{(missing.Count == 1 ? "y" : "ies")} referenced in {file} but missing from Install.Orchestrator.bat: "
                + string.Join(", ", missing)
                + " — a fresh orchestrated install would leave the WiX write without a value.");
        }
    }

    [TestMethod]
    public void EveryKafkaMsiPropertyReferencedInWxs_HasAMappingInMsiJson()
    {
        var mappings = MsiParameterMappings();
        Assert.IsTrue(mappings.Count > 0, "No KAFKA.* MSIParameter mappings found in Setup.Dorc.msi.json — the extraction regex or the file layout changed.");

        foreach (var (file, wxs) in WxsFiles())
        {
            var missing = KafkaMsiProperties(wxs).Except(mappings.Keys).OrderBy(p => p).ToList();
            Assert.AreEqual(0, missing.Count,
                $"MSI propert{(missing.Count == 1 ? "y" : "ies")} referenced in {file} but missing from Setup.Dorc.msi.json: "
                + string.Join(", ", missing)
                + " — DOrc-driven installs would never pass the value through.");
        }
    }

    [TestMethod]
    public void EveryKafkaDeployPropertyInMsiJson_IsSeededInDeploySettingsTemplate()
    {
        var mappings = MsiParameterMappings();
        var seeded = TemplateDeployProperties();
        Assert.IsTrue(seeded.Count > 0, "No KAFKA_* properties found in DeploySettings.template.json — the extraction regex or the file layout changed.");

        var missing = mappings.Values.Except(seeded).OrderBy(p => p).ToList();
        Assert.AreEqual(0, missing.Count,
            "KAFKA_* DeployProperty(ies) mapped in Setup.Dorc.msi.json but missing from src/install-scripts/DeploySettings.template.json: "
            + string.Join(", ", missing)
            + " — new environments seeded from the template would install without them.");
    }

    private static IEnumerable<(string File, string Content)> WxsFiles()
    {
        yield return ("ProdActionService.wxs", ProdWxs);
        yield return ("NonProdActionService.wxs", NonProdWxs);
        yield return ("RequestApi.wxs", RequestApiWxs);
    }
}
