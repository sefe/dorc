using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface ICloudResourceAuditPersistentSource
    {
        void InsertCloudResourceAudit(string username, ActionType action, int? cloudResourceId, string? fromValue, string? toValue);

        GetComponentAuditListResponseDto GetCloudResourceAuditByCloudResourceId(int cloudResourceId, int limit, int page, PagedDataOperators operators);

        GetComponentAuditListResponseDto GetCloudResourceAudit(int limit, int page, PagedDataOperators operators);
    }
}
