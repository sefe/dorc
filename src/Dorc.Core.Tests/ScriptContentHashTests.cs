using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests;

[TestClass]
public class ScriptContentHashTests
{
    [TestMethod]
    public void ComputeFromScriptText_MatchesPublishedSha256Vector()
    {
        Assert.AreEqual(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ScriptContentHash.ComputeFromScriptText("abc"));
    }

    [TestMethod]
    [DataRow("utf8-nobom", "\n")]
    [DataRow("utf8-bom", "\r\n")]
    [DataRow("utf16-le-bom", "\r\n")]
    public void ReadAndHash_UnifiesEncodedScriptFilesWithPowerShellInputFingerprint(
        string encodingKind,
        string newline)
    {
        var directory = Path.Join(
            AppContext.BaseDirectory,
            "script-content-hash-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var scriptPath = Path.Join(directory, "Deploy.ps1");
            var expectedText = "Write-Output 'café'" + newline + "# exact script body";
            Encoding encoding = encodingKind switch
            {
                "utf8-nobom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                "utf8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                "utf16-le-bom" => Encoding.Unicode,
                _ => throw new ArgumentOutOfRangeException(nameof(encodingKind))
            };

            File.WriteAllText(scriptPath, expectedText, encoding);

            var evidencePath = Path.Join(directory, "0.json");
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new PowerShellEvidence
            {
                RequestId = 10,
                ResultId = 20,
                ScriptOrdinal = 0,
                DefinitionRevision = "rev-1",
                Phase = "Allocated",
                SourcePath = scriptPath
            }));

            var (scriptText, hash) = ScriptContentHash.ReadAndHash(scriptPath);
            var fileHash = ScriptContentHash.ComputeFromFile(scriptPath);
            var runnerInput = new PowerShellInput(scriptPath, directory, 10, 20, "rev-1", 0, "7", "7.4");

            Assert.AreEqual(expectedText, scriptText);
            Assert.AreEqual(expectedText, runnerInput.Text);
            Assert.AreEqual(ScriptContentHash.ComputeFromScriptText(expectedText), hash);
            Assert.AreEqual(hash, fileHash);
            Assert.AreEqual(runnerInput.TextSha256.ToLowerInvariant(), hash);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ReadAndHash_ReadsOnceAndPreservesInMemoryTextWhenDiskMutatesAfterRead()
    {
        var directory = Path.Join(
            AppContext.BaseDirectory,
            "script-content-hash-single-read",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var scriptPath = Path.Join(directory, "Deploy.ps1");
            const string initialBody = "Write-Output 'approved'";
            File.WriteAllText(scriptPath, initialBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var (scriptText, hash) = ScriptContentHash.ReadAndHash(scriptPath);
            File.WriteAllText(scriptPath, "Write-Output 'tampered-after-read'");

            Assert.AreEqual(initialBody, scriptText);
            Assert.AreEqual(ScriptContentHash.ComputeFromScriptText(initialBody), hash);
            Assert.IsFalse(ScriptContentHash.Matches(hash, ScriptContentHash.ComputeFromFile(scriptPath)));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    [DataRow("not-a-hash")]
    [DataRow("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")]
    [DataRow("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08a")]
    [DataRow("zf86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void IsWellFormed_AndTryNormalize_RejectInvalidSha256Strings(string candidate)
    {
        Assert.IsFalse(ScriptContentHash.IsWellFormed(candidate));
        Assert.IsFalse(ScriptContentHash.TryNormalize(candidate, out var normalized));
        Assert.IsNull(normalized);
        Assert.IsFalse(ScriptContentHash.Matches(candidate, candidate));
    }

    [TestMethod]
    public void TryNormalize_LowercasesValidHexAndClearsNullOrWhitespace()
    {
        var hash = ScriptContentHash.ComputeFromScriptText("abc");

        Assert.IsTrue(ScriptContentHash.TryNormalize($"  {hash.ToUpperInvariant()}  ", out var normalized));
        Assert.AreEqual(hash, normalized);

        Assert.IsTrue(ScriptContentHash.TryNormalize(null!, out var clearedNull));
        Assert.IsNull(clearedNull);

        Assert.IsTrue(ScriptContentHash.TryNormalize("   ", out var clearedSpace));
        Assert.IsNull(clearedSpace);
    }

    [TestMethod]
    public void RetainForPath_PreservesOnlyOnExactOrdinalUnchangedPath()
    {
        var hash = ScriptContentHash.ComputeFromScriptText("abc");

        Assert.AreEqual(hash, ScriptContentHash.RetainForPath(hash, @"sub\Deploy.ps1", @"sub\Deploy.ps1"));
        Assert.IsNull(ScriptContentHash.RetainForPath(hash, @"sub\Deploy.ps1", @"sub\Other.ps1"));
        Assert.IsNull(ScriptContentHash.RetainForPath(hash, @"sub\Deploy.ps1", @"sub\deploy.ps1"));
        Assert.IsNull(ScriptContentHash.RetainForPath(hash, @"sub\Deploy.ps1", @"sub\Deploy.ps1 "));
        Assert.IsNull(ScriptContentHash.RetainForPath(null!, @"sub\Deploy.ps1", @"sub\Deploy.ps1"));
    }

    [TestMethod]
    public void ReadAndHash_AndComputeFromFile_RejectEmptyNulAndDirectoryOnlyPaths()
    {
        var nulPath = Path.Join(AppContext.BaseDirectory, "Deploy\0.ps1");
        var rootOnlyPath = Path.GetPathRoot(AppContext.BaseDirectory)!;
        var directoryOnlyPath = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var invalidPath in new string?[]
        {
            null,
            "",
            "   ",
            "Deploy\0.ps1",
            nulPath,
            rootOnlyPath,
            directoryOnlyPath
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() => ScriptContentHash.ReadAndHash(invalidPath!));
            Assert.ThrowsExactly<ArgumentException>(() => ScriptContentHash.ComputeFromFile(invalidPath!));
        }
    }

    [TestMethod]
    public void ReadAndHash_AndComputeFromFile_AcceptDoubleDotFileNamesAndRejectParentTraversalOrRelativePaths()
    {
        var rootDirectory = Path.Join(
            AppContext.BaseDirectory,
            "script-content-hash-paths",
            Guid.NewGuid().ToString("N"));
        var childSubDirectory = Path.Join(rootDirectory, "sub");
        Directory.CreateDirectory(childSubDirectory);

        try
        {
            const string scriptBody = "Write-Output 'hash-me'";
            var expectedHash = ScriptContentHash.ComputeFromScriptText(scriptBody);

            var doubleDotNamePath = Path.Join(childSubDirectory, "Deploy..ps1");
            var standardPath = Path.Join(rootDirectory, "Deploy.ps1");
            File.WriteAllText(doubleDotNamePath, scriptBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllText(standardPath, scriptBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            foreach (var validPath in new[] { standardPath, doubleDotNamePath })
            {
                var (text, hash) = ScriptContentHash.ReadAndHash(validPath);
                Assert.AreEqual(scriptBody, text);
                Assert.AreEqual(expectedHash, hash);
                Assert.AreEqual(expectedHash, ScriptContentHash.ComputeFromFile(validPath));
            }

            var parentSegmentPath = childSubDirectory
                + Path.DirectorySeparatorChar
                + ".."
                + Path.DirectorySeparatorChar
                + "Deploy.ps1";
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), standardPath);
            Assert.IsFalse(Path.IsPathRooted(relativePath));

            foreach (var rejectedPath in new[] { parentSegmentPath, relativePath, "Deploy.ps1" })
            {
                Assert.ThrowsExactly<ArgumentException>(() => ScriptContentHash.ReadAndHash(rejectedPath));
                Assert.ThrowsExactly<ArgumentException>(() => ScriptContentHash.ComputeFromFile(rejectedPath));
            }
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    [DataRow(null, ScriptContentVerificationMode.Report)]
    [DataRow("", ScriptContentVerificationMode.Report)]
    [DataRow("   ", ScriptContentVerificationMode.Report)]
    [DataRow("Off", ScriptContentVerificationMode.Off)]
    [DataRow("off", ScriptContentVerificationMode.Off)]
    [DataRow(" Off ", ScriptContentVerificationMode.Off)]
    [DataRow("Report", ScriptContentVerificationMode.Report)]
    [DataRow("report", ScriptContentVerificationMode.Report)]
    [DataRow(" REPORT ", ScriptContentVerificationMode.Report)]
    [DataRow("Enforce", ScriptContentVerificationMode.Enforce)]
    [DataRow("enforce", ScriptContentVerificationMode.Enforce)]
    [DataRow("ENFORCE", ScriptContentVerificationMode.Enforce)]
    [DataRow(" Enforce ", ScriptContentVerificationMode.Enforce)]
    [DataRow("Off, Enforce", ScriptContentVerificationMode.Report)]
    [DataRow("Off, Off", ScriptContentVerificationMode.Report)]
    [DataRow("Report, Enforce", ScriptContentVerificationMode.Report)]
    [DataRow("unknown-mode", ScriptContentVerificationMode.Report)]
    [DataRow("0", ScriptContentVerificationMode.Report)]
    [DataRow("2", ScriptContentVerificationMode.Report)]
    [DataRow("99", ScriptContentVerificationMode.Report)]
    public void ConfigurationSettings_ParsesScriptContentVerificationModeSafely(
        string? configuredValue,
        ScriptContentVerificationMode expected)
    {
        var values = new Dictionary<string, string?>();
        if (configuredValue != null)
        {
            values["AppSettings:ScriptContentVerification"] = configuredValue;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var settings = new ConfigurationSettings(configuration);

        Assert.AreEqual(expected, settings.GetScriptContentVerificationMode());
    }
}
