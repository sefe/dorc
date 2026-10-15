using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class ApiRegistrationAuditPersistentSource : IApiRegistrationAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public ApiRegistrationAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void InsertApiRegistrationAudit(string username, ActionType action, int? apiRegistrationId, string? fromValue, string? toValue)
        {
            // Skip no-op Updates (matching DaemonAuditPersistentSource convention)
            if (action == ActionType.Update && string.Equals(fromValue, toValue))
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                var actionRow = context.RefDataAuditActions.First(x => x.Action == action);

                context.ApiRegistrationAudits.Add(new ApiRegistrationAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    ApiRegistrationId = apiRegistrationId,
                    RefDataAuditActionId = actionRow.RefDataAuditActionId,
                    Action = actionRow,
                    FromValue = fromValue,
                    ToValue = toValue
                });
                context.SaveChanges();
            }
        }

        public GetComponentAuditListResponseDto GetApiRegistrationAuditByApiRegistrationId(int apiRegistrationId, int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                // ApiRegistrationId is int? (nullable), which the string-based ContainsExpression
                // helper doesn't support — apply the per-record filter explicitly here and
                // keep the user-supplied filters inside the shared pipeline (matching
                // DaemonAuditPersistentSource).
                var queryable = context.ApiRegistrationAudits
                    .Include(a => a.Action)
                    .Where(a => a.ApiRegistrationId == apiRegistrationId);

                return ComponentAuditQuery.RunPagedQuery(queryable, limit, page, operators,
                    ids => ApiRegistrationNamesByIds(context, ids));
            }
        }

        public GetComponentAuditListResponseDto GetApiRegistrationAudit(int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.ApiRegistrationAudits
                    .Include(a => a.Action)
                    .AsQueryable();

                return ComponentAuditQuery.RunPagedQuery(queryable, limit, page, operators,
                    ids => ApiRegistrationNamesByIds(context, ids));
            }
        }

        private static Dictionary<int, string> ApiRegistrationNamesByIds(IDeploymentContext context, IReadOnlyCollection<int> ids)
        {
            return context.ApiRegistrations
                .Where(a => ids.Contains(a.Id))
                .AsNoTracking()
                .ToDictionary(a => a.Id, a => a.Name);
        }
    }
}
