using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

internal static class ProjectTerraformBackends
{
    internal static IQueryable<DeploymentRequestDefinition> CatalogDefinitions(IDeploymentContext context, int projectId) =>
        context.DeploymentRequestDefinitions.FromSqlRaw(
            "SELECT * FROM [deploy].[DeploymentRequestDefinition] WHERE [ProjectId] = {0} " +
            "AND JSON_VALUE([DefinitionJson], '$.TerraformSourceType') IN ({1}, {2}) " +
            "AND JSON_VALUE([DefinitionJson], '$.ComponentType') IN ({3}, {4})",
            projectId, ((int)TerraformSourceType.Catalog).ToString(System.Globalization.CultureInfo.InvariantCulture),
            nameof(TerraformSourceType.Catalog), ((int)ComponentType.Terraform).ToString(System.Globalization.CultureInfo.InvariantCulture),
            nameof(ComponentType.Terraform));

    internal static void ValidateChange(IDeploymentContext context, Project project,
        IEnumerable<TerraformStateBackendApiModel>? backends)
    {
        var proposed = backends?.ToList() ?? [];
        TerraformStateBackends.Validate(proposed);
        var previous = JsonSerializer.Deserialize<List<TerraformStateBackendApiModel>>(project.TerraformStateBackendsJson ?? "[]")!;
        if (proposed.Count == 0 && previous.Count == 0) return;
        var mapped = context.Environments.Where(e => e.Projects.Any(p => p.Id == project.Id))
            .Select(e => new { e.Id, e.Name }).ToList();
        if (proposed.Any(b => b.EnvironmentId.HasValue &&
            !mapped.Any(e => e.Id == b.EnvironmentId) &&
            !previous.Any(old => old.EnvironmentId == b.EnvironmentId && TerraformStateBackends.SameLocation(old, b))))
            throw new ArgumentException("Terraform backend overrides must target environments mapped to this project.");

        var catalogDefinitions = CatalogDefinitions(context, project.Id);
        var scopes = mapped.Select(e => (e.Id, e.Name)).ToList();
        var submitted = (from definition in catalogDefinitions
                         join request in context.DeploymentRequests on definition.DeploymentRequestId equals request.Id
                         where !definition.IsSkipped && request.TerraformBackendRetiredByChangeId == null
                         select new { request.Environment, definition.ProjectJson }).Distinct().ToList()
            .Select(row => new
            {
                row.Environment,
                Backend = (JsonSerializer.Deserialize<ProjectApiModel>(row.ProjectJson)
                    ?? throw new InvalidOperationException("The submitted project snapshot is invalid."))
                    .TerraformStateBackends?.SingleOrDefault()
            }).ToList();
        foreach (var snapshot in submitted)
        {
            var id = snapshot.Backend?.EnvironmentId ??
                context.Environments.Where(e => e.Name == snapshot.Environment).Select(e => (int?)e.Id).SingleOrDefault() ?? 0;
            if (!scopes.Any(e => e.Id == id && (id != 0 || e.Name == snapshot.Environment)))
                scopes.Add((id, snapshot.Environment ?? ""));
        }
        foreach (var receipt in context.TerraformDeploymentReceipts.Where(r =>
                     (r.ProjectId == project.Id || (r.ProjectId == null && r.ProjectName == project.Name)) &&
                     r.TemplateName != null).Select(r => new { r.EnvironmentId, r.EnvironmentName }).Distinct().ToList())
        {
            var id = receipt.EnvironmentId ??
                context.Environments.Where(e => e.Name == receipt.EnvironmentName).Select(e => (int?)e.Id).SingleOrDefault() ?? 0;
            if (!scopes.Any(e => e.Id == id && (id != 0 || e.Name == receipt.EnvironmentName)))
                scopes.Add((id, receipt.EnvironmentName));
        }

        foreach (var environment in scopes)
        {
            var next = TerraformStateBackends.Resolve(proposed, environment.Id);
            if (TerraformStateBackends.SameLocation(TerraformStateBackends.Resolve(previous, environment.Id), next))
                continue;

            foreach (var snapshot in submitted.Where(snapshot =>
                         snapshot.Backend?.EnvironmentId == environment.Id || snapshot.Environment == environment.Name))
            {
                var pinned = snapshot.Backend;
                if (pinned != null && !TerraformStateBackends.SameLocation(pinned, next))
                    throw new ArgumentException("This Terraform backend is bound to submitted requests. Changing or removing it requires an operator-reviewed state migration; ordinary definition edits cannot move state.");
            }

            var receipts = context.TerraformDeploymentReceipts.Where(r =>
                (r.ProjectId == project.Id || (r.ProjectId == null && r.ProjectName == project.Name)) &&
                r.TemplateName != null && (r.EnvironmentId == environment.Id || r.EnvironmentName == environment.Name))
                .Select(r => new { r.StateStorageAccount, r.StateContainerName }).Distinct().ToList();
            if (receipts.Any(r => next == null || r.StateStorageAccount != next.StorageAccount ||
                                  r.StateContainerName != next.ContainerName))
                throw new ArgumentException("Retained Catalog plans identify a different state backend. Adopt the existing account/container or complete an operator-reviewed state migration first.");
        }
    }
}
