using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IContainerAuditPersistentSource
    {
        void InsertContainerAudit(string username, ActionType action, int? containerId, string? fromValue, string? toValue);
    }
}
