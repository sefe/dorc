using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class ContainerAuditPersistentSource : IContainerAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public ContainerAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void InsertContainerAudit(string username, ActionType action, int? containerId, string? fromValue, string? toValue)
        {
            // Skip no-op Updates (matching DaemonAuditPersistentSource convention)
            if (action == ActionType.Update && string.Equals(fromValue, toValue))
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                var actionRow = context.RefDataAuditActions.First(x => x.Action == action);

                context.ContainerAudits.Add(new ContainerAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    ContainerId = containerId,
                    RefDataAuditActionId = actionRow.RefDataAuditActionId,
                    Action = actionRow,
                    FromValue = fromValue,
                    ToValue = toValue
                });
                context.SaveChanges();
            }
        }

        public GetComponentAuditListResponseDto GetContainerAuditByContainerId(int containerId, int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                // ContainerId is int? (nullable), which the string-based ContainsExpression
                // helper doesn't support — apply the per-record filter explicitly here and
                // keep the user-supplied filters inside the shared pipeline (matching
                // DaemonAuditPersistentSource).
                var queryable = context.ContainerAudits
                    .Include(a => a.Action)
                    .Where(a => a.ContainerId == containerId);

                return ComponentAuditQuery.RunPagedQuery(queryable, limit, page, operators,
                    ids => ContainerNamesByIds(context, ids));
            }
        }

        public GetComponentAuditListResponseDto GetContainerAudit(int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.ContainerAudits
                    .Include(a => a.Action)
                    .AsQueryable();

                return ComponentAuditQuery.RunPagedQuery(queryable, limit, page, operators,
                    ids => ContainerNamesByIds(context, ids));
            }
        }

        private static Dictionary<int, string> ContainerNamesByIds(IDeploymentContext context, IReadOnlyCollection<int> ids)
        {
            return context.Containers
                .Where(c => ids.Contains(c.Id))
                .AsNoTracking()
                .ToDictionary(c => c.Id, c => c.Name);
        }
    }
}
