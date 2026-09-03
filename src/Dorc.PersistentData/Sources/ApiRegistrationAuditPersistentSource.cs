using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

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
    }
}
