using System.Data;
using Dorc.ApiModel;
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

    public bool ReviewTerraformInventory(int componentId, int environmentId, int cloudResourceId, long expectedRevision)
    {
        using var context = _contextFactory.GetContext();
        return context.TerraformInventoryProvenance.Where(row => row.ComponentId == componentId &&
            row.EnvironmentId == environmentId && row.CloudResourceId == cloudResourceId && row.Revision == expectedRevision &&
            row.ReconciliationState == "MissingFromLatestState")
            .ExecuteUpdate(set => set.SetProperty(row => row.ReconciliationState, "ReviewedMissing")
                .SetProperty(row => row.Revision, row => row.Revision + 1)) == 1;
    }
}
