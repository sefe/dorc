using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

internal sealed record DeploymentSourceEvidence(int ProjectId, string Revision, string Label, string ComponentName, bool IsTerraform)
{
    public static DeploymentSourceEvidence? Read(IDeploymentContext context, int requestId, int componentId, int? resultId)
    {
        var definition = context.DeploymentRequestDefinitions.AsNoTracking()
            .SingleOrDefault(d => d.DeploymentRequestId == requestId && d.ComponentId == componentId);
        if (definition == null) return null;
        var component = JsonSerializer.Deserialize<ComponentApiModel>(definition.DefinitionJson)!;
        var receipt = resultId.HasValue ? context.TerraformDeploymentReceipts.AsNoTracking()
            .SingleOrDefault(r => r.DeploymentResultId == resultId) : null;
        var powershell = resultId.HasValue ? context.PowerShellExecutionReceipts.AsNoTracking()
            .Where(r => r.DeploymentResultId == resultId).OrderByDescending(r => r.ScriptOrdinal).FirstOrDefault() : null;
        string source;
        if (receipt != null)
            source = !string.IsNullOrEmpty(receipt.GitCommitSha) ? "Planned Git " + receipt.GitCommitSha :
                !string.IsNullOrEmpty(receipt.TemplateName) ? $"Planned Catalog {receipt.TemplateName}@{receipt.TemplateVersion} · {receipt.SourceSha256}" :
                $"Planned {receipt.SourceType} · source SHA256 {receipt.SourceSha256}";
        else if (powershell != null && !string.IsNullOrEmpty(powershell.TextSha256))
            source = $"{(powershell.InvocationObservedAt.HasValue ? "Observed" : "Prepared")} PowerShell text SHA256 {powershell.TextSha256}";
        else
        {
            var build = context.DeploymentRequests.Where(r => r.Id == requestId).Select(r => r.BuildNumber).FirstOrDefault();
            source = component.ComponentType == ComponentType.PowerShell ? "Submitted PowerShell definition " + definition.DefinitionRevision :
                component.TerraformSourceType == TerraformSourceType.Catalog ?
                    $"Submitted Catalog {component.TerraformTemplateName}@{component.TerraformTemplateVersion}" :
                    component.TerraformSourceType == TerraformSourceType.AzureArtifact || component.TerraformSourceType == TerraformSourceType.GitHubArtifact ?
                        $"Submitted {component.TerraformSourceType} build {build ?? "(not recorded)"}" :
                        $"Submitted {component.TerraformSourceType} {component.TerraformGitBranch}".TrimEnd();
        }
        return new(definition.ProjectId, definition.DefinitionRevision, source, definition.ComponentName,
            component.ComponentType == ComponentType.Terraform);
    }
}
