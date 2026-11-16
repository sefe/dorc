using System.Data;
using System.Xml.Linq;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class RequestsPersistentSource
{
    public TerraformDeploymentOutcome? GetTerraformDeploymentOutcome(int deploymentResultId)
    {
        using var context = _contextFactory.GetContext();
        return context.TerraformDeploymentOutcomes.AsNoTracking().SingleOrDefault(row => row.DeploymentResultId == deploymentResultId);
    }

    public void SaveTerraformDeploymentOutcome(TerraformDeploymentOutcome outcome)
    {
        using var context = _contextFactory.GetContext();
        var current = context.TerraformDeploymentOutcomes.SingleOrDefault(row => row.DeploymentResultId == outcome.DeploymentResultId);
        if (current == null) context.TerraformDeploymentOutcomes.Add(outcome);
        else
        {
            if (current.Revision != outcome.Revision) throw new InvalidOperationException("Terraform outcome changed.");
            if (current.ApplyOutcome == "Succeeded" && outcome.ApplyOutcome != "Succeeded")
                throw new InvalidOperationException("A successful apply outcome cannot be replaced.");
            current.ApplyOutcome = outcome.ApplyOutcome;
            current.InventoryOutcome = outcome.InventoryOutcome;
            current.ArchiveOutcome = outcome.ArchiveOutcome;
            current.RegisteredCount = outcome.RegisteredCount;
            current.FailedCount = outcome.FailedCount;
            current.AppliedAt = outcome.AppliedAt;
            current.InventoryArtifactSha256 = outcome.InventoryArtifactSha256;
            current.InventoryWarningsJson = outcome.InventoryWarningsJson;
            current.Revision++;
            outcome.Revision = current.Revision;
        }
        context.SaveChanges();
    }

    public TerraformDeploymentOutcome RecoverTerraformPostApply(int deploymentResultId, long expectedRevision,
        Action<TerraformDeploymentOutcome> recover, bool touchesInventory = true)
    {
        using var strategyContext = _contextFactory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = _contextFactory.GetContext();
            // A database-held lock, not an expiring claim: a crashed worker releases it.
            // Recovery operations are idempotent and never execute Terraform apply.
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var outcome = context.TerraformDeploymentOutcomes
                .FromSqlRaw("SELECT * FROM [deploy].[TerraformDeploymentOutcome] WITH (UPDLOCK,HOLDLOCK)")
                .Single(row => row.DeploymentResultId == deploymentResultId);
            if (outcome.Revision != expectedRevision || outcome.ApplyOutcome != "Succeeded")
                throw new InvalidOperationException("Reload the receipt. Recovery requires an unchanged successful apply outcome.");
            var receipt = context.TerraformDeploymentReceipts.Single(row => row.DeploymentResultId == deploymentResultId);
            if (touchesInventory && (from other in context.TerraformDeploymentOutcomes
                 join planned in context.TerraformDeploymentReceipts on other.DeploymentResultId equals planned.DeploymentResultId
                 where planned.ComponentId == receipt.ComponentId && planned.EnvironmentName == receipt.EnvironmentName &&
                       other.ApplyOutcome == "Succeeded" && other.AppliedAt > outcome.AppliedAt
                 select other.DeploymentResultId).Any())
                throw new InvalidOperationException("A newer apply supersedes this inventory. Recover the latest successful apply or review historical metadata manually.");
            recover(outcome);
            outcome.Revision++;
            context.SaveChanges();
            transaction.Commit();
            return outcome;
        });
    }

    public void RecordTerraformInventory(int componentId, int environmentId, int resultId, DateTimeOffset appliedAt,
        IReadOnlyList<TerraformInventoryObservation> observed, bool complete)
    {
        using var strategyContext = _contextFactory.GetContext();
        strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = _contextFactory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var rows = context.TerraformInventoryProvenance
                .FromSqlRaw("SELECT * FROM [deploy].[TerraformInventoryProvenance] WITH (UPDLOCK,HOLDLOCK)")
                .Where(row => row.ComponentId == componentId && row.EnvironmentId == environmentId).ToList();
            var environmentName = context.Environments.Where(row => row.Id == environmentId).Select(row => row.Name).SingleOrDefault();
            if ((from other in context.TerraformDeploymentOutcomes
                 join planned in context.TerraformDeploymentReceipts on other.DeploymentResultId equals planned.DeploymentResultId
                 where planned.ComponentId == componentId && planned.EnvironmentName == environmentName &&
                       other.ApplyOutcome == "Succeeded" && other.AppliedAt > appliedAt
                 select other.DeploymentResultId).Any()) return;
            // Retrying an old registration must never stale a newer successful inventory.
            if (rows.Any(row => row.LastSeenAt > appliedAt)) return;
            foreach (var observation in observed)
            {
                var row = rows.SingleOrDefault(item => item.CloudResourceId == observation.CloudResourceId);
                if (row == null)
                {
                    row = new TerraformInventoryProvenance
                    {
                        ComponentId = componentId, EnvironmentId = environmentId,
                        CloudResourceId = observation.CloudResourceId, FirstSeenResultId = resultId
                    };
                    rows.Add(row);
                    context.TerraformInventoryProvenance.Add(row);
                }
                row.DatabaseId = observation.DatabaseId;
                row.ResourceIdentifier = observation.ResourceIdentifier;
                row.LastSeenResultId = resultId;
                row.LastSeenAt = appliedAt;
                row.ReconciliationState = "Observed";
                row.Revision++;
            }
            if (complete)
            {
                var seen = observed.Select(row => row.CloudResourceId).ToHashSet();
                foreach (var row in rows.Where(row => !seen.Contains(row.CloudResourceId)))
                {
                    row.ReconciliationState = "MissingFromLatestState";
                    row.Revision++;
                }
            }
            context.SaveChanges();
            transaction.Commit();
        });
    }

    public IReadOnlyList<TerraformInventoryProvenanceApiModel> GetTerraformInventory(int componentId, int environmentId)
    {
        using var context = _contextFactory.GetContext();
        return context.TerraformInventoryProvenance.Where(row => row.ComponentId == componentId && row.EnvironmentId == environmentId)
            .Select(row => new TerraformInventoryProvenanceApiModel
            {
                ComponentId = row.ComponentId, EnvironmentId = row.EnvironmentId, CloudResourceId = row.CloudResourceId,
                DatabaseId = row.DatabaseId, FirstSeenResultId = row.FirstSeenResultId, LastSeenResultId = row.LastSeenResultId,
                LastSeenAt = row.LastSeenAt, ResourceIdentifier = row.ResourceIdentifier,
                ReconciliationState = row.ReconciliationState, Revision = row.Revision
            }).ToList();
    }

    public IReadOnlyList<TerraformInventoryProvenanceApiModel> GetTerraformInventoryForEnvironment(int environmentId, int? deploymentResultId = null)
    {
        using var context = _contextFactory.GetContext();
        var query = context.TerraformInventoryProvenance.AsNoTracking()
            .Where(row => row.EnvironmentId == environmentId);
        if (deploymentResultId.HasValue)
        {
            query = FilterInventoryByDeploymentResult(context, query, deploymentResultId.Value);
        }
        return query.Select(row => new TerraformInventoryProvenanceApiModel
        {
            ComponentId = row.ComponentId,
            EnvironmentId = row.EnvironmentId,
            CloudResourceId = row.CloudResourceId,
            DatabaseId = row.DatabaseId,
            FirstSeenResultId = row.FirstSeenResultId,
            LastSeenResultId = row.LastSeenResultId,
            LastSeenAt = row.LastSeenAt,
            ResourceIdentifier = row.ResourceIdentifier,
            ReconciliationState = row.ReconciliationState,
            Revision = row.Revision
        }).ToList();
    }

    public void DeleteTerraformInventoryProvenance(int environmentId, int? deploymentResultId = null)
    {
        using var context = _contextFactory.GetContext();
        var query = context.TerraformInventoryProvenance
            .Where(row => row.EnvironmentId == environmentId);
        if (deploymentResultId.HasValue)
        {
            query = FilterInventoryByDeploymentResult(context, query, deploymentResultId.Value);
        }
        var rows = query.ToList();
        if (rows.Count > 0)
        {
            context.TerraformInventoryProvenance.RemoveRange(rows);
            context.SaveChanges();
        }
    }

    public bool ReviewTerraformInventory(int componentId, int environmentId, int cloudResourceId, long expectedRevision)
    {
        using var context = _contextFactory.GetContext();
        return context.TerraformInventoryProvenance.Where(row => row.ComponentId == componentId &&
            row.EnvironmentId == environmentId && row.CloudResourceId == cloudResourceId && row.Revision == expectedRevision &&
            row.ReconciliationState == "MissingFromLatestState")
            .ExecuteUpdate(set => set.SetProperty(row => row.ReconciliationState, "ReviewedMissing")
                .SetProperty(row => row.Revision, row => row.Revision + 1)) == 1;
    }

    public IReadOnlyList<TerraformDeploymentReceipt> GetTerraformReceiptsForEnvironment(string environmentName)
    {
        using var context = _contextFactory.GetContext();
        return context.TerraformDeploymentReceipts
            .AsNoTracking()
            .Where(r => r.EnvironmentName == environmentName)
            .ToList();
    }

    public IReadOnlyList<int> GetProvisionedComponentIdsForEnvironment(int environmentId)
    {
        if (environmentId <= 0)
            return Array.Empty<int>();

        using var context = _contextFactory.GetContext();
        var seenIds = new HashSet<int>();
        var seenProjectsByComponent = new Dictionary<int, HashSet<string>>();
        var orderedIds = new List<int>();

        void AddOccurrence(int componentId, string? project)
        {
            if (componentId <= 0)
                return;

            if (!string.IsNullOrWhiteSpace(project))
            {
                if (!seenProjectsByComponent.TryGetValue(componentId, out var projects))
                {
                    projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    seenProjectsByComponent[componentId] = projects;
                }

                if (projects.Add(project) && (seenIds.Add(componentId) || projects.Count > 1))
                {
                    orderedIds.Add(componentId);
                }
            }
            else if (seenIds.Add(componentId))
            {
                orderedIds.Add(componentId);
            }
        }

        foreach (var item in (
            from r in context.TerraformDeploymentReceipts.AsNoTracking()
            where r.EnvironmentId == environmentId && r.Operation != "Destroy"
            join req in context.DeploymentRequests.AsNoTracking() on r.DeploymentRequestId equals req.Id into reqGroup
            from req in reqGroup.DefaultIfEmpty()
            orderby r.DeploymentResultId
            select new { r.ComponentId, r.ProjectName, RequestProject = req != null ? req.Project : null }))
        {
            var project = !string.IsNullOrWhiteSpace(item.ProjectName) ? item.ProjectName : item.RequestProject;
            AddOccurrence(item.ComponentId, project);
        }

        foreach (var item in (
            from p in context.TerraformInventoryProvenance.AsNoTracking()
            where p.EnvironmentId == environmentId
            join res in context.DeploymentResults.AsNoTracking() on p.LastSeenResultId equals res.Id into resGroup
            from res in resGroup.DefaultIfEmpty()
            join rec in context.TerraformDeploymentReceipts.AsNoTracking() on p.LastSeenResultId equals rec.DeploymentResultId into recGroup
            from rec in recGroup.DefaultIfEmpty()
            orderby p.FirstSeenResultId
            select new
            {
                p.ComponentId,
                ResultProject = res != null ? res.DeploymentRequest.Project : null,
                ReceiptProject = rec != null ? rec.ProjectName : null
            }))
        {
            var project = !string.IsNullOrWhiteSpace(item.ResultProject) ? item.ResultProject : item.ReceiptProject;
            AddOccurrence(item.ComponentId, project);
        }

        return orderedIds;
    }

    public IReadOnlyList<string> GetProvisionedProjectsForComponent(int environmentId, int componentId)
    {
        if (environmentId <= 0 || componentId <= 0)
            return Array.Empty<string>();

        using var context = _contextFactory.GetContext();
        var projects = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in (
            from r in context.TerraformDeploymentReceipts.AsNoTracking()
            where r.EnvironmentId == environmentId && r.ComponentId == componentId && r.Operation != "Destroy"
            join req in context.DeploymentRequests.AsNoTracking() on r.DeploymentRequestId equals req.Id into reqGroup
            from req in reqGroup.DefaultIfEmpty()
            orderby r.DeploymentResultId
            select new { r.ProjectName, RequestProject = req != null ? req.Project : null }))
        {
            var proj = !string.IsNullOrWhiteSpace(item.ProjectName) ? item.ProjectName : item.RequestProject;
            if (!string.IsNullOrWhiteSpace(proj) && seen.Add(proj))
            {
                projects.Add(proj);
            }
        }

        foreach (var item in (
            from p in context.TerraformInventoryProvenance.AsNoTracking()
            where p.EnvironmentId == environmentId && p.ComponentId == componentId
            join res in context.DeploymentResults.AsNoTracking() on p.LastSeenResultId equals res.Id into resGroup
            from res in resGroup.DefaultIfEmpty()
            join rec in context.TerraformDeploymentReceipts.AsNoTracking() on p.LastSeenResultId equals rec.DeploymentResultId into recGroup
            from rec in recGroup.DefaultIfEmpty()
            orderby p.FirstSeenResultId
            select new
            {
                ResultProject = res != null ? res.DeploymentRequest.Project : null,
                ReceiptProject = rec != null ? rec.ProjectName : null
            }))
        {
            var proj = !string.IsNullOrWhiteSpace(item.ResultProject) ? item.ResultProject : item.ReceiptProject;
            if (!string.IsNullOrWhiteSpace(proj) && seen.Add(proj))
            {
                projects.Add(proj);
            }
        }

        return projects;
    }

    public IReadOnlyList<(ProjectApiModel Project, ComponentApiModel Component)> GetProvisionedComponentDefinitionsForEnvironment(int environmentId)
    {
        if (environmentId <= 0)
            return Array.Empty<(ProjectApiModel, ComponentApiModel)>();

        using var context = _contextFactory.GetContext();
        var envName = context.Environments.AsNoTracking()
            .Where(e => e.Id == environmentId)
            .Select(e => e.Name)
            .SingleOrDefault();
        if (string.IsNullOrWhiteSpace(envName))
            return Array.Empty<(ProjectApiModel, ComponentApiModel)>();

        var rows = (
            from d in context.DeploymentRequestDefinitions.AsNoTracking()
            join r in context.DeploymentRequests.AsNoTracking() on d.DeploymentRequestId equals r.Id
            where r.Environment == envName && !d.IsSkipped
            orderby d.DeploymentRequestId descending, d.Ordinal
            select new { r.RequestDetails, d.ProjectId, d.ComponentId, d.ProjectJson, d.DefinitionJson })
            .ToList();

        var results = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
        var seen = new HashSet<(int ProjectId, int ComponentId)>();
        foreach (var row in rows)
        {
            if (IsDestroyRequestXml(row.RequestDetails))
                continue;

            var component = System.Text.Json.JsonSerializer.Deserialize<ComponentApiModel>(row.DefinitionJson)!;
            if (component.ComponentType == ComponentType.Terraform && seen.Add((row.ProjectId, row.ComponentId)))
            {
                var project = System.Text.Json.JsonSerializer.Deserialize<ProjectApiModel>(row.ProjectJson)!;
                results.Add((project, component));
            }
        }

        return results;
    }

    public IReadOnlyList<RequestProperty> GetProvisionedRequestPropertiesForEnvironment(
        int environmentId,
        string? projectName = null)
    {
        using var context = _contextFactory.GetContext();
        var env = context.Environments.AsNoTracking()
            .FirstOrDefault(e => e.Id == environmentId);
        if (env is null)
            return Array.Empty<RequestProperty>();

        var currentNamesById = context.Components.AsNoTracking()
            .Where(c => c.Name != null && c.Name != "")
            .ToDictionary(c => c.Id, c => c.Name!);

        var candidateReceipts = context.TerraformDeploymentReceipts.AsNoTracking()
            .Where(r => r.EnvironmentId == environmentId &&
                        r.Operation != "Destroy" &&
                        (projectName == null || r.ProjectName == projectName))
            .ToList();
        var candidateResultIds = candidateReceipts.Select(r => r.DeploymentResultId).ToHashSet();
        var outcomesByResultId = candidateResultIds.Count > 0
            ? context.TerraformDeploymentOutcomes.AsNoTracking()
                .Where(o => candidateResultIds.Contains(o.DeploymentResultId))
                .Select(o => new { o.DeploymentResultId, o.ApplyOutcome })
                .ToDictionary(o => o.DeploymentResultId, o => o.ApplyOutcome)
            : new Dictionary<int, string>();

        var selectedReceipts = candidateReceipts
            .GroupBy(r => (r.ProjectName, r.ComponentId))
            .Select(g => TerraformApplyReceiptSelection.SelectStateMutatingReceipt(
                g.Select(r => (r, outcomesByResultId.GetValueOrDefault(r.DeploymentResultId))),
                allowNeverRanFallback: true)!)
            .ToList();
        var receiptComponentMapByRequestId = new Dictionary<int, Dictionary<string, HashSet<string>>>();
        foreach (var r in selectedReceipts)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(r.ComponentName))
            {
                targets.Add(r.ComponentName);
            }
            if (r.ComponentId is int cid &&
                currentNamesById.TryGetValue(cid, out var cn) &&
                !cn.EndsWith($" (deleted-{cid})", StringComparison.Ordinal))
            {
                targets.Add(cn);
            }
            if (targets.Count == 0)
                continue;

            if (!receiptComponentMapByRequestId.TryGetValue(r.DeploymentRequestId, out var map))
            {
                map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                receiptComponentMapByRequestId[r.DeploymentRequestId] = map;
            }
            foreach (var t in targets)
            {
                map[t] = targets;
            }
        }

        var otherIncarnationRequestIds = context.TerraformDeploymentReceipts.AsNoTracking()
            .Where(r => r.EnvironmentName == env.Name &&
                        r.EnvironmentId != null &&
                        r.EnvironmentId != environmentId)
            .Select(r => r.DeploymentRequestId)
            .ToHashSet();

        var candidateRequests = context.DeploymentRequests.AsNoTracking()
            .Where(r => r.Environment == env.Name &&
                        r.RequestDetails != null &&
                        r.RequestDetails != "" &&
                        (projectName == null || r.Project == projectName) &&
                        (receiptComponentMapByRequestId.Count > 0
                            ? receiptComponentMapByRequestId.Keys.Contains(r.Id)
                            : (r.Status != "Cancelled" && !otherIncarnationRequestIds.Contains(r.Id))))
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.RequestDetails, Components = r.Components ?? "" })
            .ToList();

        var merged = new Dictionary<string, RequestProperty>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in candidateRequests)
        {
            if (IsDestroyRequestXml(req.RequestDetails))
                continue;

            var compMap = receiptComponentMapByRequestId.TryGetValue(req.Id, out var rcMap)
                ? rcMap
                : req.Components.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(c => c, c => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { c }, StringComparer.OrdinalIgnoreCase);
            var targetComponents = compMap.Values.SelectMany(v => v).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var existingKey in merged.Keys.ToList())
            {
                if (targetComponents.Contains(existingKey[..existingKey.LastIndexOf(':')]))
                    merged.Remove(existingKey);
            }

            var explicitScopedKeysInReq = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propElem in System.Xml.Linq.XElement.Parse(req.RequestDetails).Descendants("PropertyPair"))
            {
                var name = (string)propElem.Element("Name")!;
                if (TerraformDestroyOperationConstants.IsReservedLifecycleProperty(name))
                    continue;

                var val = (string)propElem.Element("Value")!;
                var isSens = string.Equals((string?)propElem.Element("IsSensitive"), "true", StringComparison.OrdinalIgnoreCase);
                var colonIdx = name.LastIndexOf(':');
                if (colonIdx > 0)
                {
                    if (compMap.TryGetValue(name[..colonIdx], out var targetComps))
                    {
                        foreach (var targetComp in targetComps)
                        {
                            var scopedName = $"{targetComp}:{name[(colonIdx + 1)..]}";
                            merged[scopedName] = new RequestProperty { PropertyName = scopedName, PropertyValue = val, IsSensitive = isSens };
                            explicitScopedKeysInReq.Add(scopedName);
                        }
                    }
                }
                else
                {
                    foreach (var comp in targetComponents)
                    {
                        var scopedName = $"{comp}:{name}";
                        if (!explicitScopedKeysInReq.Contains(scopedName))
                        {
                            merged[scopedName] = new RequestProperty { PropertyName = scopedName, PropertyValue = val, IsSensitive = isSens };
                        }
                    }
                }
            }
        }

        return merged.Values.ToList();
    }

    public DeploymentRequestApiModel? GetLatestAppliedRequestForEnvironment(
        string environmentName,
        string? projectName = null,
        int? componentId = null)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
            return null;

        using var context = _contextFactory.GetContext();
        var currentEnvironmentId = context.Environments
            .AsNoTracking()
            .Where(e => e.Name == environmentName)
            .Select(e => (int?)e.Id)
            .FirstOrDefault();

        if (componentId is > 0)
        {
            var receiptRows = context.TerraformDeploymentReceipts
                .AsNoTracking()
                .Where(r => r.EnvironmentName == environmentName &&
                            (currentEnvironmentId == null || r.EnvironmentId == null || r.EnvironmentId == currentEnvironmentId) &&
                            r.ComponentId == componentId.Value &&
                            r.Operation != "Destroy")
                .OrderByDescending(r => r.DeploymentResultId)
                .Select(r => new
                {
                    r.DeploymentResultId,
                    r.DeploymentRequestId,
                    IsApplied = r.Operation == "Apply" ||
                                context.TerraformDeploymentOutcomes.Any(o =>
                                    o.DeploymentResultId == r.DeploymentResultId &&
                                    o.ApplyOutcome != TerraformDeploymentOutcome.ApplyNotStarted &&
                                    o.ApplyOutcome != "NotAttempted")
                })
                .ToList();

            foreach (var row in receiptRows
                .OrderByDescending(r => r.IsApplied)
                .ThenByDescending(r => r.DeploymentResultId))
            {
                var receiptReq = context.DeploymentRequests
                    .AsNoTracking()
                    .FirstOrDefault(r => r.Id == row.DeploymentRequestId);
                if (receiptReq is not null &&
                    (projectName == null || string.Equals(receiptReq.Project, projectName, StringComparison.OrdinalIgnoreCase)) &&
                    (row.IsApplied || !string.Equals(receiptReq.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) &&
                    HasUsableArtifactBuildMetadata(receiptReq.BuildUri, receiptReq.DropLocation))
                {
                    return new DeploymentRequestApiModel
                    {
                        Id = receiptReq.Id,
                        Project = receiptReq.Project,
                        EnvironmentName = receiptReq.Environment,
                        BuildUri = receiptReq.BuildUri,
                        DropLocation = receiptReq.DropLocation,
                        BuildNumber = receiptReq.BuildNumber,
                        RequestDetails = receiptReq.RequestDetails
                    };
                }
            }
        }

        var appliedRequestIds = context.TerraformDeploymentReceipts
            .AsNoTracking()
            .Where(r => r.EnvironmentName == environmentName &&
                        (currentEnvironmentId == null || r.EnvironmentId == null || r.EnvironmentId == currentEnvironmentId) &&
                        r.Operation != "Destroy" &&
                        (r.Operation == "Apply" ||
                         context.TerraformDeploymentOutcomes.Any(o =>
                             o.DeploymentResultId == r.DeploymentResultId &&
                             o.ApplyOutcome != TerraformDeploymentOutcome.ApplyNotStarted &&
                             o.ApplyOutcome != "NotAttempted")))
            .Select(r => r.DeploymentRequestId)
            .ToHashSet();

        var candidates = context.DeploymentRequests
            .AsNoTracking()
            .Where(r => r.Environment == environmentName &&
                        (projectName == null || r.Project == projectName))
            .OrderByDescending(r => r.Id)
            .ToList();

        foreach (var candidate in candidates
            .OrderByDescending(r => appliedRequestIds.Contains(r.Id))
            .ThenByDescending(r => r.Id))
        {
            if (!IsDestroyRequestXml(candidate.RequestDetails) &&
                (appliedRequestIds.Contains(candidate.Id) || !string.Equals(candidate.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) &&
                HasUsableArtifactBuildMetadata(candidate.BuildUri, candidate.DropLocation))
            {
                return new DeploymentRequestApiModel
                {
                    Id = candidate.Id,
                    Project = candidate.Project,
                    EnvironmentName = candidate.Environment,
                    BuildUri = candidate.BuildUri,
                    DropLocation = candidate.DropLocation,
                    BuildNumber = candidate.BuildNumber,
                    RequestDetails = candidate.RequestDetails
                };
            }
        }

        return null;
    }

    public void BackfillRequestBuildMetadata(
        int requestId,
        string? buildUri,
        string? dropLocation,
        string? buildNumber = null)
    {
        if (requestId <= 0 ||
            (string.IsNullOrWhiteSpace(buildUri) &&
             string.IsNullOrWhiteSpace(dropLocation) &&
             string.IsNullOrWhiteSpace(buildNumber)))
        {
            return;
        }

        using var context = _contextFactory.GetContext();
        var req = context.DeploymentRequests.FirstOrDefault(r => r.Id == requestId);
        if (req is null)
        {
            return;
        }

        var root = string.IsNullOrWhiteSpace(req.RequestDetails)
            ? new XElement("DeploymentRequestDetail")
            : XElement.Parse(req.RequestDetails);
        var buildDetail = root.Element("BuildDetail");
        if (buildDetail is null)
        {
            buildDetail = new XElement("BuildDetail");
            root.AddFirst(buildDetail);
        }

        if (!string.IsNullOrWhiteSpace(buildUri))
        {
            buildDetail.SetElementValue("Uri", buildUri);
        }

        if (!string.IsNullOrWhiteSpace(dropLocation))
        {
            buildDetail.SetElementValue("DropLocation", dropLocation);
        }

        if (!string.IsNullOrWhiteSpace(buildNumber))
        {
            buildDetail.SetElementValue("BuildNumber", buildNumber);
            req.BuildNumber = buildNumber;
        }

        req.RequestDetails = root.ToString(SaveOptions.DisableFormatting);
        context.SaveChanges();
    }

    private static bool HasUsableArtifactBuildMetadata(string? buildUri, string? dropLocation)
    {
        return IsNonCatalogBuildToken(buildUri) || IsNonCatalogBuildToken(dropLocation);
    }

    private static bool IsNonCatalogBuildToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               !string.Equals(token, "catalog://bundled", StringComparison.OrdinalIgnoreCase) &&
               !token.StartsWith("dorc-catalog://", StringComparison.OrdinalIgnoreCase);
    }

    internal static IQueryable<TerraformInventoryProvenance> FilterInventoryByDeploymentResult(
        IDeploymentContext context,
        IQueryable<TerraformInventoryProvenance> query,
        int resultId)
    {
        var componentId = (context.DeploymentResults != null
            ? context.DeploymentResults
                .Where(r => r.Id == resultId)
                .Select(r => (int?)r.Component.Id)
                .SingleOrDefault()
            : null)
            ?? (context.TerraformDeploymentReceipts != null
                ? context.TerraformDeploymentReceipts
                    .Where(rec => rec.DeploymentResultId == resultId)
                    .Select(rec => (int?)rec.ComponentId)
                    .FirstOrDefault()
                : null);

        if (!componentId.HasValue)
        {
            return query.Where(row => row.FirstSeenResultId == resultId || row.LastSeenResultId == resultId);
        }

        var targetProject = (context.DeploymentResults != null
            ? context.DeploymentResults
                .Where(target => target.Id == resultId && target.DeploymentRequest != null)
                .Select(target => target.DeploymentRequest.Project)
                .FirstOrDefault()
            : null)
            ?? (context.TerraformDeploymentReceipts != null
                ? context.TerraformDeploymentReceipts
                    .Where(rec => rec.DeploymentResultId == resultId && rec.ProjectName != "")
                    .Select(rec => rec.ProjectName)
                    .FirstOrDefault()
                : null);

        var otherProjectResultIds = (context.DeploymentResults != null
                ? context.DeploymentResults
                    .Where(r => r.Component.Id == componentId.Value &&
                                r.DeploymentRequest != null &&
                                r.DeploymentRequest.Project != targetProject)
                    .Select(r => r.Id)
                : Enumerable.Empty<int>().AsQueryable())
            .Union(context.TerraformDeploymentReceipts != null
                ? context.TerraformDeploymentReceipts
                    .Where(rec => rec.ComponentId == componentId.Value &&
                                  rec.ProjectName != "" &&
                                  rec.ProjectName != targetProject)
                    .Select(rec => rec.DeploymentResultId)
                : Enumerable.Empty<int>().AsQueryable());

        return query.Where(row =>
            (row.ComponentId == componentId.Value &&
             !otherProjectResultIds.Contains(row.LastSeenResultId) &&
             !otherProjectResultIds.Contains(row.FirstSeenResultId)) ||
            row.FirstSeenResultId == resultId ||
            row.LastSeenResultId == resultId);
    }

    private static readonly System.Text.RegularExpressions.Regex EphemeralEnvironmentIdXmlRegex = new(
        $@"<Name>\s*{System.Text.RegularExpressions.Regex.Escape(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName)}\s*</Name>\s*<Value>\s*([1-9]\d*)\s*</Value>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static int? TryGetExpectedEphemeralEnvironmentId(string? requestDetailsXml)
    {
        var match = EphemeralEnvironmentIdXmlRegex.Match(requestDetailsXml ?? string.Empty);
        return match.Success
            ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    public static void EnsureEphemeralEnvironmentAcceptsSubmission(
        Dorc.PersistentData.Contexts.IDeploymentContext context,
        DeploymentRequest deploymentRequest)
    {
        var expectedEnvironmentId = TryGetExpectedEphemeralEnvironmentId(deploymentRequest.RequestDetails);
        if (string.IsNullOrWhiteSpace(deploymentRequest.Environment) && !expectedEnvironmentId.HasValue)
        {
            return;
        }

        var environments = context.Environments.Where(r => r.Name == deploymentRequest.Environment).ToList();
        if (environments.Count == 0 ||
            (expectedEnvironmentId.HasValue && !environments.Any(e => e.Id == expectedEnvironmentId.Value)))
        {
            throw new InvalidOperationException(
                $"Cannot submit deployments to environment '{deploymentRequest.Environment}' because the target environment no longer exists.");
        }

        foreach (var environment in environments)
        {
            EnsureEphemeralEnvironmentAcceptsSubmission(environment, deploymentRequest);
        }
    }

    public static void EnsureEphemeralEnvironmentAcceptsSubmission(
        Dorc.PersistentData.Model.Environment environment,
        DeploymentRequest deploymentRequest)
    {
        var isDestroy = IsDestroyRequestXml(deploymentRequest.RequestDetails);
        var hasTargetState = HasLifecycleTargetStatePropertyXml(deploymentRequest.RequestDetails);
        if (!environment.IsEphemeral)
        {
            if (isDestroy || hasTargetState)
            {
                throw new InvalidOperationException(
                    $"Ephemeral lifecycle requests cannot be submitted to non-ephemeral environment '{environment.Name}'.");
            }
            return;
        }

        var expectedEnvironmentId = TryGetExpectedEphemeralEnvironmentId(deploymentRequest.RequestDetails);
        if ((isDestroy || hasTargetState || expectedEnvironmentId.HasValue) &&
            environment.Id > 0 &&
            expectedEnvironmentId != environment.Id)
        {
            throw new InvalidOperationException(
                $"Ephemeral lifecycle request for '{environment.Name}' requires a matching '{TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName}'.");
        }

        if (isDestroy)
        {
            if (environment.IsProd ||
                !string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Destroy requests can only be submitted to non-production ephemeral environments in '{EphemeralEnvironmentStates.TearingDown}' state.");
            }
            return;
        }

        if ((string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.Provisioning, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.Testing, StringComparison.OrdinalIgnoreCase)) &&
            !hasTargetState)
        {
            throw new InvalidOperationException(
                $"Cannot submit non-lifecycle deployments to ephemeral environment '{environment.Name}' while it is in '{environment.EphemeralState}' state.");
        }

        if (!string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase))
        {
            if (!expectedEnvironmentId.HasValue && environment.Id > 0)
            {
                deploymentRequest.RequestDetails = StampEphemeralEnvironmentIdXml(deploymentRequest.RequestDetails, environment.Id);
            }
            return;
        }

        throw new InvalidOperationException(
            $"Cannot submit deployments to ephemeral environment '{environment.Name}' while it is in '{environment.EphemeralState}' state.");
    }

    public static string StampEphemeralEnvironmentIdXml(string? requestDetailsXml, int environmentId)
    {
        var doc = string.IsNullOrWhiteSpace(requestDetailsXml)
            ? new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("DeploymentRequestDetail"))
            : System.Xml.Linq.XDocument.Parse(requestDetailsXml);
        var root = doc.Root!;
        var properties = root.Element("Properties");
        if (properties == null)
        {
            properties = new System.Xml.Linq.XElement("Properties");
            root.Add(properties);
        }

        properties.Add(new System.Xml.Linq.XElement(
            "PropertyPair",
            new System.Xml.Linq.XElement("Name", TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName),
            new System.Xml.Linq.XElement("Value", environmentId.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return doc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    public static bool HasLifecycleTargetStatePropertyXml(string? requestDetailsXml)
    {
        if (string.IsNullOrWhiteSpace(requestDetailsXml))
        {
            return false;
        }

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(requestDetailsXml);
            foreach (var pair in doc.Descendants("PropertyPair"))
            {
                var name = pair.Element("Name")?.Value;
                if (string.Equals(name, TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, TerraformDestroyOperationConstants.RunValidationRequestPropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore non-XML or malformed request details.
        }

        return false;
    }

    public static bool IsDestroyRequestXml(string? requestDetailsXml)
    {
        if (string.IsNullOrWhiteSpace(requestDetailsXml))
        {
            return false;
        }

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(requestDetailsXml);
            foreach (var pair in doc.Descendants("PropertyPair"))
            {
                var name = pair.Element("Name")?.Value;
                var value = pair.Element("Value")?.Value;
                if ((string.Equals(name, TerraformDestroyOperationConstants.PropertyName, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(value, TerraformDestroyOperationConstants.DestroyValue, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(name, TerraformDestroyOperationConstants.IsDestroyPropertyName, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore non-XML or malformed request details.
        }

        return false;
    }
}
