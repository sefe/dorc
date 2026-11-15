using System.Text.Json;
using System.Xml.Linq;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class RequestsPersistentSource
{
    public void EnsureRequestBackendNotRetired(int requestId)
    {
        using var context = _contextFactory.GetContext();
        if (context.DeploymentRequests.Any(r => r.Id == requestId && r.TerraformBackendRetiredByChangeId != null))
            throw new TerraformBackendRetiredException();
    }

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
    internal static List<DeploymentRequestDefinition> CaptureDefinitions(IDeploymentContext context, DeploymentRequest request)
    {
        EnsureEphemeralEnvironmentAcceptsSubmission(context, request);
        var isDestroy = IsDestroyRequestXml(request.RequestDetails);
        var xml = XElement.Parse(request.RequestDetails ?? "<root/>");
        var explicitDestroyIds = isDestroy
            ? xml.Descendants("PropertyPair")
                .Where(p => string.Equals((string?)p.Element("Name"), TerraformDestroyOperationConstants.DestroyComponentIdsPropertyName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Element("Value")!.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
                .ToHashSet()
            : new HashSet<int>();
        var priorByComponentId = isDestroy
            ? (from d in context.DeploymentRequestDefinitions.AsNoTracking()
               join r in context.DeploymentRequests.AsNoTracking() on d.DeploymentRequestId equals r.Id
               where r.Environment == request.Environment && r.Project == request.Project && !d.IsSkipped
               orderby d.DeploymentRequestId descending
               select new { r.RequestDetails, Definition = d })
              .AsEnumerable()
              .Where(x => !IsDestroyRequestXml(x.RequestDetails) &&
                          JsonSerializer.Deserialize<ComponentApiModel>(x.Definition.DefinitionJson)!.ComponentType == ComponentType.Terraform)
              .GroupBy(x => x.Definition.ComponentId)
              .ToDictionary(g => g.Key, g => g.First().Definition)
            : new Dictionary<int, DeploymentRequestDefinition>();

        var project = context.Projects.FromSqlRaw("SELECT * FROM [deploy].[Project] WITH (UPDLOCK,HOLDLOCK)")
            .SingleOrDefault(p => p.Name == request.Project)
            ?? throw new InvalidOperationException("The request project no longer exists.");
        var current = DeploymentDefinitions.Read(context, project.Id);
        var selected = (request.Components ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = DeploymentDefinitions.Flatten(current.Components)
            .Where(c => !isDestroy || c.ComponentType == ComponentType.Terraform)
            .ToList();
        if (selected.Count == 0 || selected.Any(name =>
                !graph.Any(c => c.ComponentName.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                !priorByComponentId.Values.Any(d => d.ComponentName.Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Every selected component must belong to the request project.");
        DeploymentDefinitions.ValidateNonSecret(current.Project.TerraformGitRepoUrl);
        DeploymentDefinitions.ValidateNonSecret(current.Project.ArtefactsUrl);
        var skipped = xml.Descendants("ComponentsToSkip").Descendants()
            .Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<DeploymentRequestDefinition>();
        foreach (var component in graph.Where(c =>
            selected.Contains(c.ComponentName) &&
            (explicitDestroyIds.Count == 0 || explicitDestroyIds.Contains(c.ComponentId!.Value))))
        {
            DeploymentDefinitions.ValidateNonSecret(component.ScriptPath);
            DeploymentDefinitions.ValidateKnownSensitiveArguments(context, component.ScriptPath);
            component.Children = new List<ComponentApiModel>();
            var revision = DeploymentDefinitions.Observe(context, current.Project, component);
            var script = context.Components.Where(c => c.Id == component.ComponentId).Select(c => c.Script).SingleOrDefault();
            var submittedProject = JsonSerializer.Deserialize<ProjectApiModel>(JsonSerializer.Serialize(current.Project))!;
            if (component.ComponentType == ComponentType.Terraform &&
                component.TerraformSourceType == TerraformSourceType.Catalog)
            {
                if (skipped.Contains(component.ComponentName))
                {
                    submittedProject.TerraformStateBackends = [];
                }
                else
                {
                    var environmentId = context.Environments.Where(e => e.Name == request.Environment &&
                        (isDestroy || e.Projects.Any(p => p.Id == project.Id))).Select(e => (int?)e.Id).SingleOrDefault()
                        ?? throw new InvalidOperationException("The Catalog request environment must be mapped to its project.");
                    var backend = TerraformStateBackends.Require(current.Project.TerraformStateBackends, environmentId);
                    // Only the effective backend travels with this request. It must not be
                    // reselected from live mappings or overridden by request properties.
                    submittedProject.TerraformStateBackends =
                    [
                        new TerraformStateBackendApiModel
                        {
                            EnvironmentId = environmentId,
                            StorageAccount = backend.StorageAccount, ContainerName = backend.ContainerName,
                            SubscriptionId = backend.SubscriptionId, ResourceGroup = backend.ResourceGroup
                        }
                    ];
                }
            }
            result.Add(new DeploymentRequestDefinition
            {
                Ordinal = result.Count, ProjectId = project.Id, ComponentId = component.ComponentId!.Value,
                ComponentName = component.ComponentName, DefinitionRevisionId = revision.Id,
                DefinitionRevision = revision.Revision, DefinitionJson = JsonSerializer.Serialize(component),
                ProjectJson = JsonSerializer.Serialize(submittedProject), IsSkipped = skipped.Contains(component.ComponentName),
                ScriptJson = script == null ? null : JsonSerializer.Serialize(new ScriptApiModel
                {
                    Id = script.Id, Name = script.Name, Path = script.Path, IsPathJSON = script.IsPathJSON,
                    NonProdOnly = script.NonProdOnly, PowerShellVersionNumber = script.PowerShellVersionNumber,
                    IsEnabled = component.IsEnabled, Revision = revision.Revision
                }),
                SubmittedAt = request.RequestedTime ?? DateTimeOffset.UtcNow, SubmittedBy = request.UserName ?? ""
            });
        }
        var historicalDefinitions = priorByComponentId.Values
            .Where(d => (explicitDestroyIds.Count > 0
                ? explicitDestroyIds.Contains(d.ComponentId)
                : selected.Contains(d.ComponentName)) &&
                result.All(r => r.ComponentId != d.ComponentId));
        foreach (var priorDef in historicalDefinitions)
        {
            EnsureComponentRowExists(context, priorDef.ComponentId, priorDef.DefinitionJson);
            result.Add(new DeploymentRequestDefinition
            {
                Ordinal = result.Count,
                ProjectId = priorDef.ProjectId,
                ComponentId = priorDef.ComponentId,
                ComponentName = priorDef.ComponentName,
                DefinitionRevisionId = priorDef.DefinitionRevisionId,
                DefinitionRevision = priorDef.DefinitionRevision,
                DefinitionJson = priorDef.DefinitionJson,
                ProjectJson = priorDef.ProjectJson,
                IsSkipped = skipped.Contains(priorDef.ComponentName),
                ScriptJson = priorDef.ScriptJson,
                SubmittedAt = request.RequestedTime ?? DateTimeOffset.UtcNow,
                SubmittedBy = request.UserName ?? ""
            });
        }
        return result;
    }

    internal static Component EnsureComponentRowExists(
        IDeploymentContext context,
        int componentId,
        string definitionJson)
    {
        var existing = context.Components.FirstOrDefault(c => c.Id == componentId);
        if (existing is not null)
            return existing;

        var pinned = JsonSerializer.Deserialize<ComponentApiModel>(definitionJson)!;
        const int maxComponentNameLength = 64;
        var baseName = pinned.ComponentName ?? string.Empty;
        var suffix = $" (deleted-{componentId})";
        var maxPrefixLength = Math.Max(0, maxComponentNameLength - suffix.Length);
        var truncatedPrefix = baseName.Length > maxPrefixLength
            ? baseName.Substring(0, maxPrefixLength)
            : baseName;
        var tombstoneName = context.Components.Any(c => c.Name == baseName)
            ? $"{truncatedPrefix}{suffix}"
            : baseName;
        var tombstone = new Component
        {
            Id = componentId,
            Name = tombstoneName,
            ObjectId = Guid.NewGuid(),
            Description = "Restored tombstone for destroy",
            IsEnabled = pinned.IsEnabled,
            StopOnFailure = pinned.StopOnFailure,
            ComponentType = pinned.ComponentType,
            TerraformSourceType = pinned.TerraformSourceType,
            TerraformGitBranch = pinned.TerraformGitBranch,
            TerraformSubPath = pinned.TerraformSubPath,
            TerraformTemplateName = pinned.TerraformTemplateName,
            TerraformTemplateVersion = pinned.TerraformTemplateVersion
        };
        context.Components.Add(tombstone);
        context.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [deploy].[Component] ON");
        try
        {
            context.SaveChanges();
        }
        finally
        {
            context.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [deploy].[Component] OFF");
        }
        return tombstone;
    }

    internal static List<CatalogRequestInputs> CaptureDestroyCatalogInputs(
        IDeploymentContext context,
        DeploymentRequest request,
        List<DeploymentRequestDefinition> definitions)
    {
        var environmentId = context.Environments.Where(environment => environment.Name == request.Environment)
            .Select(environment => (int?)environment.Id).SingleOrDefault();
        var pinnedReceiptsByComponent = context.TerraformDeploymentReceipts
            .Where(r => r.EnvironmentName == request.Environment &&
                        (environmentId == null || r.EnvironmentId == null || r.EnvironmentId == environmentId) &&
                        (request.Project == null || request.Project == "" || r.ProjectName == null || r.ProjectName == "" || r.ProjectName == request.Project) &&
                        r.Operation != "Destroy" &&
                        (r.Operation == "Apply" || context.TerraformDeploymentOutcomes.Any(o => o.DeploymentResultId == r.DeploymentResultId)))
            .ToList()
            .GroupBy(r => r.ComponentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.DeploymentResultId)
                      .First());

        var snapshots = new List<CatalogRequestInputs>();
        foreach (var definition in definitions)
        {
            var component = JsonSerializer.Deserialize<ComponentApiModel>(definition.DefinitionJson)!;
            pinnedReceiptsByComponent.TryGetValue(definition.ComponentId, out var pinnedReceipt);
            var priorSnapshot = pinnedReceipt is not null
                ? context.CatalogRequestInputs.SingleOrDefault(row =>
                    row.DeploymentRequestId == pinnedReceipt.DeploymentRequestId &&
                    row.ComponentId == definition.ComponentId)
                : null;
            var configuration = environmentId.HasValue
                ? context.CatalogEnvironmentInputs.SingleOrDefault(row =>
                    row.ComponentId == definition.ComponentId && row.EnvironmentId == environmentId)
                : null;
            if (component.TerraformSourceType != TerraformSourceType.Catalog &&
                priorSnapshot is null &&
                configuration is null)
                continue;

            var inputsJson = priorSnapshot?.InputsJson ?? configuration?.InputsJson ?? "{}";
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(inputsJson)!;
            RejectSecureCatalogInputs(context, values.Keys);
            snapshots.Add(new CatalogRequestInputs
            {
                ComponentId = definition.ComponentId,
                TemplateName = priorSnapshot?.TemplateName ?? component.TerraformTemplateName ?? component.ComponentName,
                TemplateVersion = priorSnapshot?.TemplateVersion
                    ?? (!string.IsNullOrWhiteSpace(configuration?.TemplateVersion)
                        ? configuration.TemplateVersion
                        : component.TerraformTemplateVersion!),
                ConfigurationRevision = priorSnapshot?.ConfigurationRevision ?? configuration?.Revision ?? 0,
                InputsJson = JsonSerializer.Serialize(values)
            });
        }
        return snapshots;
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
        EnsureRequestBackendNotRetired(requestId);
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

    public ProjectApiModel GetSubmittedProject(int requestId, int componentId)
    {
        EnsureRequestBackendNotRetired(requestId);
        return JsonSerializer.Deserialize<ProjectApiModel>(GetRequestDefinitions(requestId)
            .Single(x => x.ComponentId == componentId).ProjectJson)!;
    }
}
