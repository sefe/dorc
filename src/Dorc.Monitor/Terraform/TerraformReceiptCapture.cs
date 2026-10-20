using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Dorc.Core.AzureStorageAccount;
using Dorc.PersistentData.Model;

namespace Dorc.Monitor.Terraform;

internal static class TerraformReceiptCapture
{
    internal static void ReadPlannedSource(TerraformDeploymentReceipt receipt,
        string sourceArchivePath, string planContentPath, string planPath)
    {
        using var archive = ZipFile.OpenRead(sourceArchivePath);
        var entries = archive.Entries.Where(entry => entry.FullName == "_dorc_provenance.json").ToArray();
        if (entries.Length != 1 || entries[0].Length > 64 * 1024)
            throw new InvalidDataException("The planned source has missing or invalid provenance.");
        using var stream = entries[0].Open();
        using var document = JsonDocument.Parse(stream);
        var source = document.RootElement;
        if (source.GetProperty("RequestId").GetInt32() != receipt.DeploymentRequestId ||
            source.GetProperty("DeploymentResultId").GetInt32() != receipt.DeploymentResultId)
            throw new InvalidDataException("The planned source provenance does not match the deployment.");
        string? Text(string name) => source.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (receipt.DefinitionRevision != null && Text("DefinitionRevision") != receipt.DefinitionRevision)
            throw new InvalidDataException("The planned source does not match the submitted definition revision.");
        receipt.TemplateName = Text("TemplateName");
        receipt.TemplateVersion = Text("TemplateVersion");
        receipt.GitCommitSha = Text("GitCommitSha");
        receipt.StateStorageAccount = Text("StateStorageAccount");
        receipt.StateContainerName = Text("StateContainerName");
        receipt.StateKey = Text("StateKey");
        receipt.WorkloadTarget = Text("WorkloadTarget");
        receipt.PlannedAt = source.GetProperty("ArchivedAtUtc").GetDateTimeOffset();
        using var sourceFile = File.OpenRead(sourceArchivePath);
        receipt.SourceSha256 = Convert.ToHexString(SHA256.HashData(sourceFile));
        using var planFile = File.OpenRead(planPath);
        receipt.PlanSha256 = Convert.ToHexString(SHA256.HashData(planFile));
        receipt.PlanContentSha256 = TerraformPlanArtifactIntegrity.HashPlanContent(File.ReadAllText(planContentPath));
    }
}
