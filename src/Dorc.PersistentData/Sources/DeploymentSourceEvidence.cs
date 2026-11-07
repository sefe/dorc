using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

internal sealed record DeploymentSourceEvidence(int ProjectId, string Revision, string Label, string ComponentName, bool IsTerraform)
{
    public readonly record struct Key(int RequestId, int ComponentId, int? ResultId);

    public static DeploymentSourceEvidence? Read(IDeploymentContext context, int requestId, int componentId, int? resultId)
    {
        var key = new Key(requestId, componentId, resultId);
        return ReadMany(context, new[] { key }).GetValueOrDefault(key);
    }

    // Loads the evidence for every key with one query per evidence table
    // instead of three or four per row; the result lists this feeds are
    // polled by the UI.
    public static IReadOnlyDictionary<Key, DeploymentSourceEvidence> ReadMany(IDeploymentContext context, IEnumerable<Key> keys)
    {
        var wanted = keys.Distinct().ToList();
        var result = new Dictionary<Key, DeploymentSourceEvidence>();
        if (wanted.Count == 0) return result;

        var requestIds = wanted.Select(k => k.RequestId).Distinct().ToList();
        var resultIds = wanted.Where(k => k.ResultId.HasValue).Select(k => k.ResultId!.Value).Distinct().ToList();

        var definitions = context.DeploymentRequestDefinitions.AsNoTracking()
            .Where(d => requestIds.Contains(d.DeploymentRequestId))
            .ToDictionary(d => (d.DeploymentRequestId, d.ComponentId));
        var receipts = resultIds.Count == 0
            ? new Dictionary<int, TerraformDeploymentReceipt>()
            : context.TerraformDeploymentReceipts.AsNoTracking()
                .Where(r => resultIds.Contains(r.DeploymentResultId))
                .ToDictionary(r => r.DeploymentResultId);
        var powershellReceipts = resultIds.Count == 0
            ? new Dictionary<int, PowerShellExecutionReceipt>()
            : context.PowerShellExecutionReceipts.AsNoTracking()
                .Where(r => resultIds.Contains(r.DeploymentResultId))
                .AsEnumerable()
                .GroupBy(r => r.DeploymentResultId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ScriptOrdinal).First());
        var buildNumbers = context.DeploymentRequests.AsNoTracking()
            .Where(r => requestIds.Contains(r.Id))
            .Select(r => new { r.Id, r.BuildNumber })
            .ToDictionary(r => r.Id, r => r.BuildNumber);

        foreach (var key in wanted)
        {
            if (!definitions.TryGetValue((key.RequestId, key.ComponentId), out var definition)) continue;
            var receipt = key.ResultId.HasValue ? receipts.GetValueOrDefault(key.ResultId.Value) : null;
            var powershell = key.ResultId.HasValue ? powershellReceipts.GetValueOrDefault(key.ResultId.Value) : null;
            result[key] = Build(definition, receipt, powershell, buildNumbers.GetValueOrDefault(key.RequestId));
        }
        return result;
    }

    private static DeploymentSourceEvidence Build(DeploymentRequestDefinition definition,
        TerraformDeploymentReceipt? receipt, PowerShellExecutionReceipt? powershell, string? build)
    {
        var component = JsonSerializer.Deserialize<ComponentApiModel>(definition.DefinitionJson)!;
        string source;
        if (receipt != null)
            source = !string.IsNullOrEmpty(receipt.GitCommitSha) ? "Planned Git " + receipt.GitCommitSha :
                !string.IsNullOrEmpty(receipt.TemplateName) ? $"Planned Catalog {receipt.TemplateName}@{receipt.TemplateVersion} · {receipt.SourceSha256}" :
                $"Planned {receipt.SourceType} · source SHA256 {receipt.SourceSha256}";
        else if (powershell != null && !string.IsNullOrEmpty(powershell.TextSha256))
            source = $"{(powershell.InvocationObservedAt.HasValue ? "Observed" : "Prepared")} PowerShell text SHA256 {powershell.TextSha256}";
        else
            source = component.ComponentType == ComponentType.PowerShell ? "Submitted PowerShell definition " + definition.DefinitionRevision :
                component.TerraformSourceType == TerraformSourceType.Catalog ?
                    $"Submitted Catalog {component.TerraformTemplateName}@{component.TerraformTemplateVersion}" :
                    component.TerraformSourceType == TerraformSourceType.AzureArtifact || component.TerraformSourceType == TerraformSourceType.GitHubArtifact ?
                        $"Submitted {component.TerraformSourceType} build {build ?? "(not recorded)"}" :
                        $"Submitted {component.TerraformSourceType} {component.TerraformGitBranch}".TrimEnd();
        return new(definition.ProjectId, definition.DefinitionRevision, source, definition.ComponentName,
            component.ComponentType == ComponentType.Terraform);
    }
}
