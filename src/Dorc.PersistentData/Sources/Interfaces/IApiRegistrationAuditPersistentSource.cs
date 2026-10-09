using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IApiRegistrationAuditPersistentSource
    {
        void InsertApiRegistrationAudit(string username, ActionType action, int? apiRegistrationId, string? fromValue, string? toValue);

        GetComponentAuditListResponseDto GetApiRegistrationAuditByApiRegistrationId(int apiRegistrationId, int limit, int page, PagedDataOperators operators);

        GetComponentAuditListResponseDto GetApiRegistrationAudit(int limit, int page, PagedDataOperators operators);
    }
}
