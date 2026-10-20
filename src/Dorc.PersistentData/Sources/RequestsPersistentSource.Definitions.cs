using System.Text.Json;
using System.Xml.Linq;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class RequestsPersistentSource
{
    public void SavePowerShellEvidence(PowerShellEvidence evidence)
    {
        using var context = _contextFactory.GetContext();
        var snapshot = context.DeploymentRequestDefinitions.Single(d =>
            d.DeploymentRequestId == evidence.RequestId && d.DefinitionRevision == evidence.DefinitionRevision);
        if (!context.DeploymentResults.Any(r => r.Id == evidence.ResultId &&
            r.DeploymentRequest.Id == evidence.RequestId && r.Component.Id == snapshot.ComponentId))
            throw new InvalidOperationException("Execution evidence does not belong to the expected result and definition.");
        var existing = context.PowerShellExecutionReceipts.SingleOrDefault(r =>
            r.DeploymentResultId == evidence.ResultId && r.ScriptOrdinal == evidence.ScriptOrdinal);
        if (existing != null)
        {
            if (existing.DeploymentRequestId != evidence.RequestId || existing.DefinitionRevision != evidence.DefinitionRevision ||
                existing.TextSha256 != (evidence.TextSha256 ?? "") || existing.Phase != evidence.Phase)
                throw new InvalidOperationException("Durable execution evidence cannot be replaced.");
            return;
        }
        context.PowerShellExecutionReceipts.Add(new PowerShellExecutionReceipt
        {
            DeploymentResultId = evidence.ResultId, ScriptOrdinal = evidence.ScriptOrdinal,
            DeploymentRequestId = evidence.RequestId, ProjectId = snapshot.ProjectId, ComponentId = snapshot.ComponentId,
            DefinitionRevision = evidence.DefinitionRevision, TextSha256 = evidence.TextSha256 ?? "",
            RequestedRuntime = evidence.RequestedRuntime ?? "", RuntimeVersion = evidence.RuntimeVersion ?? "",
            SourcePath = evidence.SourcePath, BuildIdentity = evidence.BuildIdentity,
            Phase = evidence.Phase, PreparedAt = evidence.PreparedAt,
            ObservedAt = DateTimeOffset.UtcNow,
            InvocationObservedAt = evidence.InvocationObservedAt, CompletedAt = evidence.CompletedAt
        });
        context.SaveChanges();
    }
    private static List<DeploymentRequestDefinition> CaptureDefinitions(IDeploymentContext context, DeploymentRequest request)
    {
        var project = context.Projects.FromSqlRaw("SELECT * FROM [deploy].[Project] WITH (UPDLOCK,HOLDLOCK)")
            .SingleOrDefault(p => p.Name == request.Project)
            ?? throw new InvalidOperationException("The request project no longer exists.");
        var current = DeploymentDefinitions.Read(context, project.Id);
        var selected = (request.Components ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = DeploymentDefinitions.Flatten(current.Components).ToList();
        if (selected.Count == 0 || selected.Any(name => !graph.Any(c => c.ComponentName.Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Every selected component must belong to the request project.");
        DeploymentDefinitions.ValidateNonSecret(current.Project.TerraformGitRepoUrl);
        DeploymentDefinitions.ValidateNonSecret(current.Project.ArtefactsUrl);
        var xml = XElement.Parse(request.RequestDetails ?? "<root/>");
        var skipped = xml.Descendants("ComponentsToSkip").Descendants()
            .Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<DeploymentRequestDefinition>();
        foreach (var component in graph.Where(c => selected.Contains(c.ComponentName)))
        {
            DeploymentDefinitions.ValidateNonSecret(component.ScriptPath);
            DeploymentDefinitions.ValidateKnownSensitiveArguments(context, component.ScriptPath);
            component.Children = new List<ComponentApiModel>();
            var revision = DeploymentDefinitions.Observe(context, current.Project, component);
            var script = context.Components.Where(c => c.Id == component.ComponentId).Select(c => c.Script).SingleOrDefault();
            result.Add(new DeploymentRequestDefinition
            {
                Ordinal = result.Count, ProjectId = project.Id, ComponentId = component.ComponentId!.Value,
                ComponentName = component.ComponentName, DefinitionRevisionId = revision.Id,
                DefinitionRevision = revision.Revision, DefinitionJson = JsonSerializer.Serialize(component),
                ProjectJson = JsonSerializer.Serialize(current.Project), IsSkipped = skipped.Contains(component.ComponentName),
                ScriptJson = script == null ? null : JsonSerializer.Serialize(new ScriptApiModel
                {
                    Id = script.Id, Name = script.Name, Path = script.Path, IsPathJSON = script.IsPathJSON,
                    NonProdOnly = script.NonProdOnly, PowerShellVersionNumber = script.PowerShellVersionNumber,
                    IsEnabled = component.IsEnabled, Revision = revision.Revision
                }),
                SubmittedAt = request.RequestedTime ?? DateTimeOffset.UtcNow, SubmittedBy = request.UserName ?? ""
            });
        }
        return result;
    }

    public IList<DeploymentRequestDefinition> GetRequestDefinitions(int requestId)
    {
        using var context = _contextFactory.GetContext();
        var snapshots = context.DeploymentRequestDefinitions.AsNoTracking()
            .Where(x => x.DeploymentRequestId == requestId).OrderBy(x => x.Ordinal).ToList();
        if (snapshots.Count == 0)
            throw new InvalidOperationException("This legacy request has no pinned definitions. Explicitly resubmit it as a new request; live-definition fallback is disabled.");
        if (snapshots.Where((x, index) => x.Ordinal != index || string.IsNullOrEmpty(x.DefinitionRevision)).Any())
            throw new InvalidOperationException("The submitted definition graph is incomplete.");
        return snapshots;
    }

    public IList<ComponentApiModel> GetSubmittedComponents(int requestId)
    {
        return GetRequestDefinitions(requestId).Where(x => !x.IsSkipped).Select(row =>
        {
            var component = JsonSerializer.Deserialize<ComponentApiModel>(row.DefinitionJson)
                ?? throw new InvalidOperationException("The submitted definition is invalid.");
            if (component.ComponentId != row.ComponentId || component.ComponentName != row.ComponentName)
                throw new InvalidOperationException("The submitted definition identity does not match.");
            return component;
        }).ToList();
    }

    public ScriptApiModel GetSubmittedScript(int requestId, int componentId)
    {
        var row = GetRequestDefinitions(requestId).Single(x => x.ComponentId == componentId);
        if (row.ScriptJson == null) throw new InvalidOperationException("This submitted component has no executable PowerShell definition.");
        var script = JsonSerializer.Deserialize<ScriptApiModel>(row.ScriptJson)!;
        if (script.Revision != row.DefinitionRevision) throw new InvalidOperationException("The submitted script revision is invalid.");
        return script;
    }

    public ProjectApiModel GetSubmittedProject(int requestId, int componentId) =>
        JsonSerializer.Deserialize<ProjectApiModel>(GetRequestDefinitions(requestId)
            .Single(x => x.ComponentId == componentId).ProjectJson)!;
}
