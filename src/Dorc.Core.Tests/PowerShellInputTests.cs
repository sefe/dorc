using System.Text;
using System.Text.Json;
using Dorc.ApiModel;

namespace Dorc.Core.Tests;

[TestClass]
public class PowerShellInputTests
{
    [TestMethod]
    [DataRow(false, "\n")]
    [DataRow(true, "\r\n")]
    public void FingerprintUsesDecodedExactInvocationText(bool bom, string newline)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "definition-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "script.ps1");
            var text = "Write-Output 'café'" + newline + "# preserved";
            File.WriteAllText(path, text, new UTF8Encoding(bom));
            var evidencePath = Path.Combine(directory, "0.json");
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new PowerShellEvidence
            { RequestId = 2, ResultId = 3, ScriptOrdinal = 0, DefinitionRevision = "revision", Phase = "Allocated", SourcePath = path }));
            var input = new PowerShellInput(path, directory, 2, 3, "revision", 0, "7", "7.4");
            File.WriteAllText(path, "changed after preparation");
            Assert.AreEqual(text, input.Text);
            Assert.AreEqual(PowerShellInput.Fingerprint(text), input.TextSha256);
            var prepared = JsonSerializer.Deserialize<PowerShellEvidence>(File.ReadAllText(evidencePath))!;
            Assert.AreEqual("Prepared", prepared.Phase);
            Assert.IsNull(prepared.InvocationObservedAt);
            Assert.IsNull(prepared.CompletedAt);
            input.InvocationObserved();
            input.Completed();
            var completed = JsonSerializer.Deserialize<PowerShellEvidence>(File.ReadAllText(evidencePath))!;
            Assert.AreEqual("InvocationReturned", completed.Phase);
            Assert.AreEqual(input.TextSha256, completed.TextSha256);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void MissingOrWrongEvidenceIdentity_RefusesInvocationPreparation()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "definition-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Throws<InvalidOperationException>(() => new PowerShellInput("does-not-exist",
                directory, 2, 3, "revision", 0, "5", "5.1"));
            File.WriteAllText(Path.Combine(directory, "0.json"), JsonSerializer.Serialize(new PowerShellEvidence
            { RequestId = 2, ResultId = 999, DefinitionRevision = "revision", Phase = "Allocated" }));
            Assert.Throws<InvalidOperationException>(() => new PowerShellInput("does-not-exist",
                directory, 2, 3, "revision", 0, "5", "5.1"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
