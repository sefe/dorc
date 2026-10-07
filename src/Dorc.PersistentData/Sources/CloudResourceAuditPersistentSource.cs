using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class CloudResourceAuditPersistentSource : ICloudResourceAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public CloudResourceAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void InsertCloudResourceAudit(string username, ActionType action, int? cloudResourceId, string? fromValue, string? toValue)
        {
            // Skip no-op Updates (matching DaemonAuditPersistentSource convention)
            if (action == ActionType.Update && string.Equals(fromValue, toValue))
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                var actionRow = context.RefDataAuditActions.First(x => x.Action == action);

                context.CloudResourceAudits.Add(new CloudResourceAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    CloudResourceId = cloudResourceId,
                    RefDataAuditActionId = actionRow.RefDataAuditActionId,
                    Action = actionRow,
                    FromValue = fromValue,
                    ToValue = toValue
                });
                context.SaveChanges();
            }
        }

        public GetComponentAuditListResponseDto GetCloudResourceAuditByCloudResourceId(int cloudResourceId, int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                // CloudResourceId is int? (nullable), which the string-based ContainsExpression
                // helper doesn't support — apply the per-record filter explicitly here and
                // keep the user-supplied filters inside the shared pipeline (matching
                // DaemonAuditPersistentSource).
                var queryable = context.CloudResourceAudits
                    .Include(a => a.Action)
                    .Where(a => a.CloudResourceId == cloudResourceId);

                return ComponentAuditQuery.RunPagedQuery(queryable, limit, page, operators,
                    ids => CloudResourceNamesByIds(context, ids));
            }
        }

        public GetComponentAuditListResponseDto GetCloudResourceAudit(int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.CloudResourceAudits
                    .Include(a => a.Action)
                    .AsQueryable();

                return ComponentAuditQuery.RunPagedQuery(queryable, limit, page, operators,
                    ids => CloudResourceNamesByIds(context, ids));
            }
        }

        private static Dictionary<int, string> CloudResourceNamesByIds(IDeploymentContext context, IReadOnlyCollection<int> ids)
        {
            return context.CloudResources
                .Where(c => ids.Contains(c.Id))
                .AsNoTracking()
                .ToDictionary(c => c.Id, c => c.Name);
        }
    }
}
