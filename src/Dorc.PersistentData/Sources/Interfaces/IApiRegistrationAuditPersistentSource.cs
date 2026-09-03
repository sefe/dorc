using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IApiRegistrationAuditPersistentSource
    {
        void InsertApiRegistrationAudit(string username, ActionType action, int? apiRegistrationId, string? fromValue, string? toValue);
    }
}
