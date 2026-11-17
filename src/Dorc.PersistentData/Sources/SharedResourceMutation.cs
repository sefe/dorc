using System.Data;
using System.Linq.Expressions;
using System.Transactions;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public static class SharedResourceMutation
{
    public static T Execute<T>(IDeploymentContextFactory contextFactory, Func<T> mutation)
    {
        using var context = contextFactory.GetContext();
        if (context.Database == null)
        {
            return mutation();
        }

        return context.Database.CreateExecutionStrategy().Execute(() =>
        {
            // Sources use short-lived contexts. An ambient transaction keeps their
            // membership range locks and audit writes in the same atomic boundary.
            // SQL inserts from clone/registrar paths must respect these range locks too.
            using var transaction = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var result = mutation();
            transaction.Complete();
            return result;
        });
    }

    public static T ExecuteInContext<T>(IDeploymentContextFactory contextFactory, Func<IDeploymentContext, T> mutation)
    {
        return Execute(contextFactory, () =>
        {
            using var context = contextFactory.GetContext();
            return mutation(context);
        });
    }

    public static bool HasRemainingSameEnvironmentProvenance(
        IDeploymentContext context,
        int environmentId,
        int? deploymentResultId,
        Expression<Func<TerraformInventoryProvenance, bool>> resourceFilter)
    {
        if (!deploymentResultId.HasValue || context.TerraformInventoryProvenance == null)
        {
            return false;
        }

        var allForResource = context.TerraformInventoryProvenance
            .AsNoTracking()
            .Where(row => row.EnvironmentId == environmentId)
            .Where(resourceFilter);

        var scopedKeys = RequestsPersistentSource
            .FilterInventoryByDeploymentResult(context, allForResource, deploymentResultId.Value)
            .Select(row => new { row.ComponentId, row.CloudResourceId })
            .ToHashSet();

        return allForResource
            .Select(row => new { row.ComponentId, row.CloudResourceId })
            .AsEnumerable()
            .Any(key => !scopedKeys.Contains(key));
    }
}
