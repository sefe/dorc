using System.Transactions;
using Dorc.PersistentData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public static class SharedResourceMutation
{
    public static T Execute<T>(IDeploymentContextFactory contextFactory, Func<T> mutation)
    {
        using var context = contextFactory.GetContext();
        return context.Database.CreateExecutionStrategy().Execute(() =>
        {
            // Sources use short-lived contexts. An ambient transaction keeps their
            // membership range locks and audit writes in the same atomic boundary.
            // SQL inserts from clone/registrar paths must respect these range locks too.
            using var transaction = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var result = mutation();
            transaction.Complete();
            return result;
        });
    }
}
