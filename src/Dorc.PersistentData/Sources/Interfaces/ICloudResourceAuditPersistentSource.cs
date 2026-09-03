using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface ICloudResourceAuditPersistentSource
    {
        void InsertCloudResourceAudit(string username, ActionType action, int? cloudResourceId, string? fromValue, string? toValue);
    }
}
