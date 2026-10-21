using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public sealed partial class DeploymentDefinitions
{
    public RefDataApiModel CutoverEmptyTerraformBackend(int projectId, TerraformBackendCutoverApiModel input,
        string actor, Func<int, bool> canModifyProject, Func<string, bool> canModifyEnvironment)
    {
        if (string.IsNullOrWhiteSpace(input.Revision)) throw new DefinitionPreconditionException(true);
        if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException();
        if (!input.ConfirmBothContainersEmpty || input.VerificationSha256 == null ||
            !Regex.IsMatch(input.VerificationSha256, @"\A[0-9a-fA-F]{64}\z"))
            throw new ArgumentException("Confirm both containers are empty and provide the SHA-256 of the operator verification record. This operation does not migrate existing state.");
        if (input.EnvironmentId <= 0 || input.ExpectedBackend == null || input.Backend == null ||
            input.Backend.EnvironmentId != input.EnvironmentId)
            throw new ArgumentException("Specify the mapped environment, expected effective backend and a complete environment-scoped replacement.");
        TerraformStateBackends.Validate([input.ExpectedBackend]);
        TerraformStateBackends.Validate([input.Backend]);
        if (TerraformStateBackends.SameLocation(input.ExpectedBackend, input.Backend))
            throw new ArgumentException("The replacement must identify a different backend.");

        using var strategyContext = factory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = factory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var project = context.Projects.FromSqlRaw("SELECT * FROM [deploy].[Project] WITH (UPDLOCK,HOLDLOCK)")
                .Single(p => p.Id == projectId);
            if (!canModifyProject(projectId)) throw new UnauthorizedAccessException();
            var environment = context.Environments.SingleOrDefault(e => e.Id == input.EnvironmentId &&
                e.Projects.Any(p => p.Id == projectId))
                ?? throw new ArgumentException("The environment must be mapped to this project.");
            if (!canModifyEnvironment(environment.Name)) throw new UnauthorizedAccessException();
            var before = Read(context, projectId);
            RequireRevision(input.Revision, before.Revision);
            var current = TerraformStateBackends.Require(before.Project.TerraformStateBackends, environment.Id);
            if (!TerraformStateBackends.SameLocation(current, input.ExpectedBackend))
                throw new InvalidOperationException("The effective backend changed. Reload and review the cutover.");

            var candidates = (from definition in ProjectTerraformBackends.CatalogDefinitions(context, projectId)
                              join request in context.DeploymentRequests on definition.DeploymentRequestId equals request.Id
                              where !definition.IsSkipped && request.TerraformBackendRetiredByChangeId == null
                              select new { Definition = definition, request.Environment }).ToList()
                .Select(row => new
                {
                    row.Definition, row.Environment,
                    Backend = JsonSerializer.Deserialize<ProjectApiModel>(row.Definition.ProjectJson)!
                        .TerraformStateBackends?.SingleOrDefault()
                }).Where(row => row.Backend?.EnvironmentId == environment.Id || row.Environment == environment.Name).ToList();
            if (candidates.Any(row => row.Backend == null || !TerraformStateBackends.SameLocation(row.Backend, current)))
                throw new InvalidOperationException("An unretired Catalog request has missing or different backend evidence. Review it before cutover.");
            var retiringIds = candidates.Select(row => row.Definition.DeploymentRequestId).Distinct().ToArray();
            // Serializable status reads exclude a concurrent restart; submission takes the same project lock.
            var requests = context.DeploymentRequests.Where(r => retiringIds.Contains(r.Id) ||
                (r.Project == project.Name && r.Environment == environment.Name)).ToList();
            string[] terminal = ["Failed", "Errored", "Cancelled", "Canceled", "Abandoned", "Completed", "Terminated"];
            if (requests.Any(r => !terminal.Contains(r.Status)))
                throw new InvalidOperationException("All requests in the affected project/environment must be terminal before cutover.");
            var scopeIds = requests.Select(r => r.Id).ToArray();
            var observedIds = context.DeploymentRequestDefinitions
                .Where(d => scopeIds.Contains(d.DeploymentRequestId))
                .Select(d => d.DeploymentRequestId).Distinct().ToHashSet();
            if (requests.Any(r => !observedIds.Contains(r.Id)))
                throw new InvalidOperationException("Legacy requests have no pinned source/backend evidence. Locate or recover their state before changing this backend; empty containers do not prove that no infrastructure was deployed.");
            var archivedHistory = context.Database.SqlQuery<int>($"""
                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM [archive].[DeploymentRequest]
                    WHERE [Project] = {project.Name} AND [Environment] = {environment.Name}
                ) THEN 1 ELSE 0 END AS int) AS [Value]
                """).Single();
            if (archivedHistory != 0)
                throw new InvalidOperationException("Archived deployment history requires state review before cutover. This empty-backend operation cannot establish its backend ownership.");
            if (context.DeploymentRequestProcesses.Any(p => retiringIds.Contains(p.DeploymentRequest.Id)))
                throw new InvalidOperationException("A retained runner process prevents cutover. Confirm it has stopped first.");
            if (context.TerraformPlanApprovals.Any(a => retiringIds.Contains(a.DeploymentRequestId)) ||
                context.TerraformDeploymentReceipts.Any(r => r.TemplateName != null &&
                    (r.ProjectId == projectId || (r.ProjectId == null && r.ProjectName == project.Name)) &&
                    (r.EnvironmentId == environment.Id || r.EnvironmentName == environment.Name)))
                throw new InvalidOperationException("Retained Terraform plan or execution evidence prevents an empty-backend cutover. Existing state requires a separate reviewed migration.");

            var backends = before.Project.TerraformStateBackends
                .Where(b => b.EnvironmentId != environment.Id).ToList();
            backends.Add(input.Backend);
            project.TerraformStateBackendsJson = JsonSerializer.Serialize(backends);
            context.SaveChanges();
            var after = Read(context, projectId);
            var changeId = Record(context, before, after, actor, "TerraformBackend.EmptyCutover",
                "sha256:" + input.VerificationSha256.ToLowerInvariant())
                ?? throw new InvalidOperationException("The cutover did not change the project revision.");
            foreach (var request in requests.Where(r => retiringIds.Contains(r.Id)))
                request.TerraformBackendRetiredByChangeId = changeId;
            context.SaveChanges();
            transaction.Commit();
            return after;
        });
    }
}
