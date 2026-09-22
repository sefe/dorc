using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

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
    }
}
