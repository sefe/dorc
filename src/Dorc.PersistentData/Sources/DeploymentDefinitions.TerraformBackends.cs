using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public sealed partial class DeploymentDefinitions
{
    private const int MinimumDispositionLength = 20;
    private const int MaximumDispositionLength = 4000;
    private static readonly string[] TerminalStatuses =
        ["Failed", "Errored", "Cancelled", "Canceled", "Abandoned", "Completed", "Terminated"];

    private sealed record CutoverScope(
        List<DeploymentRequest> Requests,
        int[] RetiringIds,
        List<DeploymentRequest> LegacyRequests,
        List<string> Blockers);

    /// <summary>
    /// Evaluates every cutover guard for the environment's current backend without taking locks or
    /// changing anything. Blockers use the same wording the mutation throws so the UI shows what
    /// the server would say; an empty list means only the operator's container verification remains.
    /// </summary>
    public TerraformBackendCutoverPreviewApiModel PreviewTerraformBackendCutover(int projectId, int environmentId,
        Func<int, bool> canModifyProject, Func<string, bool> canModifyEnvironment)
    {
        using var context = factory.GetContext();
        var project = context.Projects.AsNoTracking().SingleOrDefault(p => p.Id == projectId)
            ?? throw new ArgumentException("Unknown project.");
        if (!canModifyProject(projectId)) throw new UnauthorizedAccessException();
        var environment = context.Environments.AsNoTracking().SingleOrDefault(e => e.Id == environmentId &&
            e.Projects.Any(p => p.Id == projectId))
            ?? throw new ArgumentException("The environment must be mapped to this project.");
        if (!canModifyEnvironment(environment.Name)) throw new UnauthorizedAccessException();
        var document = Read(context, projectId);
        var preview = new TerraformBackendCutoverPreviewApiModel
        {
            Revision = document.Revision, EnvironmentId = environment.Id, EnvironmentName = environment.Name
        };
        TerraformStateBackendApiModel? current;
        try
        {
            current = TerraformStateBackends.Resolve(document.Project.TerraformStateBackends, environment.Id);
        }
        catch (ArgumentException e)
        {
            preview.Blockers.Add(e.Message);
            return preview;
        }
        if (current == null)
        {
            preview.Blockers.Add("No Terraform state backend is configured for this environment; there is nothing to cut over.");
            return preview;
        }
        preview.CurrentBackend = current;
        var scope = AnalyseCutoverScope(context, projectId, project.Name, environment.Id, environment.Name, current);
        preview.PinnedRequestCount = scope.RetiringIds.Length;
        preview.LegacyRequests = scope.LegacyRequests.Select(r => new TerraformLegacyRequestApiModel
        {
            Id = r.Id, Status = r.Status, UserName = r.UserName, RequestedTime = r.RequestedTime, Components = r.Components
        }).ToList();
        preview.Blockers = scope.Blockers;
        return preview;
    }

    private static CutoverScope AnalyseCutoverScope(IDeploymentContext context, int projectId, string projectName,
        int environmentId, string environmentName, TerraformStateBackendApiModel current)
    {
        var blockers = new List<string>();
        var candidates = (from definition in ProjectTerraformBackends.CatalogDefinitions(context, projectId)
                          join request in context.DeploymentRequests on definition.DeploymentRequestId equals request.Id
                          where !definition.IsSkipped && request.TerraformBackendRetiredByChangeId == null
                          select new { Definition = definition, request.Environment }).ToList()
            .Select(row => new
            {
                row.Definition, row.Environment,
                Backend = JsonSerializer.Deserialize<ProjectApiModel>(row.Definition.ProjectJson)!
                    .TerraformStateBackends?.SingleOrDefault()
            }).Where(row => row.Backend?.EnvironmentId == environmentId || row.Environment == environmentName).ToList();
        if (candidates.Any(row => row.Backend == null || !TerraformStateBackends.SameLocation(row.Backend, current)))
            blockers.Add("An unretired Catalog request has missing or different backend evidence. Review it before cutover.");
        var retiringIds = candidates.Select(row => row.Definition.DeploymentRequestId).Distinct().ToArray();
        var requests = context.DeploymentRequests.Where(r => retiringIds.Contains(r.Id) ||
            (r.Project == projectName && r.Environment == environmentName)).ToList();
        if (requests.Any(r => !TerminalStatuses.Contains(r.Status)))
            blockers.Add("All requests in the affected project/environment must be terminal before cutover.");
        var scopeIds = requests.Select(r => r.Id).ToArray();
        var observedIds = context.DeploymentRequestDefinitions
            .Where(d => scopeIds.Contains(d.DeploymentRequestId))
            .Select(d => d.DeploymentRequestId).Distinct().ToHashSet();
        var legacyRequests = requests.Where(r => !observedIds.Contains(r.Id)).OrderBy(r => r.Id).ToList();
        var archivedHistory = context.Database.SqlQuery<int>($"""
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [archive].[DeploymentRequest]
                WHERE [Project] = {projectName} AND [Environment] = {environmentName}
            ) THEN 1 ELSE 0 END AS int) AS [Value]
            """).Single();
        if (archivedHistory != 0)
            blockers.Add("Archived deployment history requires state review before cutover. This empty-backend operation cannot establish its backend ownership.");
        if (context.DeploymentRequestProcesses.Any(p => retiringIds.Contains(p.DeploymentRequest.Id)))
            blockers.Add("A retained runner process prevents cutover. Confirm it has stopped first.");
        if (context.TerraformPlanApprovals.Any(a => retiringIds.Contains(a.DeploymentRequestId)) ||
            context.TerraformDeploymentReceipts.Any(r => r.TemplateName != null &&
                (r.ProjectId == projectId || (r.ProjectId == null && r.ProjectName == projectName)) &&
                (r.EnvironmentId == environmentId || r.EnvironmentName == environmentName)))
            blockers.Add("Retained Terraform plan or execution evidence prevents an empty-backend cutover. Existing state requires a separate reviewed migration.");
        return new CutoverScope(requests, retiringIds, legacyRequests, blockers);
    }

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
        var legacy = input.LegacyRetirement;
        if (legacy != null && (legacy.RequestIds == null || legacy.RequestIds.Count == 0 ||
            legacy.RequestIds.Any(id => id <= 0) || legacy.RequestIds.Distinct().Count() != legacy.RequestIds.Count ||
            string.IsNullOrWhiteSpace(legacy.StateDisposition) || legacy.StateDisposition.Trim().Length < MinimumDispositionLength ||
            legacy.StateDisposition.Length > MaximumDispositionLength))
            throw new ArgumentException($"Legacy retirement must list each reviewed request ID once and record what was done about any infrastructure they applied ({MinimumDispositionLength}-{MaximumDispositionLength} characters).");

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

            // Serializable status reads exclude a concurrent restart; submission takes the same project lock.
            var scope = AnalyseCutoverScope(context, projectId, project.Name, environment.Id, environment.Name, current);
            if (scope.Blockers.Count > 0) throw new InvalidOperationException(scope.Blockers[0]);
            var requests = scope.Requests;
            var retiringIds = scope.RetiringIds;
            var legacyRequests = scope.LegacyRequests;
            if (legacyRequests.Count > 0 && legacy == null)
                throw new InvalidOperationException("Legacy requests have no pinned source/backend evidence. Locate or recover their state before changing this backend; empty containers do not prove that no infrastructure was deployed. " +
                    $"An administrator may retire them explicitly by listing request IDs {string.Join(", ", legacyRequests.Select(r => r.Id))} with a recorded state disposition.");
            if (legacy != null && !legacyRequests.Select(r => r.Id).SequenceEqual(legacy.RequestIds.OrderBy(id => id)))
                throw new InvalidOperationException("The legacy request set differs from the one reviewed. Reload, review the current list " +
                    $"({(legacyRequests.Count == 0 ? "none" : string.Join(", ", legacyRequests.Select(r => r.Id)))}) and resubmit.");
            var operatorNote = legacy == null ? null : JsonSerializer.Serialize(new
            {
                LegacyRequestIds = legacyRequests.Select(r => r.Id).ToArray(),
                StateDisposition = legacy.StateDisposition.Trim(),
                RetiredBackend = new { input.ExpectedBackend.StorageAccount, input.ExpectedBackend.ContainerName, input.ExpectedBackend.SubscriptionId }
            });

            var backends = before.Project.TerraformStateBackends
                .Where(b => b.EnvironmentId != environment.Id).ToList();
            backends.Add(input.Backend);
            project.TerraformStateBackendsJson = JsonSerializer.Serialize(backends);
            context.SaveChanges();
            var after = Read(context, projectId);
            var changeId = Record(context, before, after, actor,
                legacy == null ? "TerraformBackend.EmptyCutover" : "TerraformBackend.LegacyCutover",
                "sha256:" + input.VerificationSha256.ToLowerInvariant(), operatorNote)
                ?? throw new InvalidOperationException("The cutover did not change the project revision.");
            foreach (var request in requests.Where(r => retiringIds.Contains(r.Id)).Concat(legacyRequests))
                request.TerraformBackendRetiredByChangeId = changeId;
            context.SaveChanges();
            transaction.Commit();
            return after;
        });
    }
}
