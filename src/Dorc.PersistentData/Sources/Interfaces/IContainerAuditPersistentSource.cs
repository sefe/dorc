using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IContainerAuditPersistentSource
    {
        void InsertContainerAudit(string username, ActionType action, int? containerId, string? fromValue, string? toValue);

        GetComponentAuditListResponseDto GetContainerAuditByContainerId(int containerId, int limit, int page, PagedDataOperators operators);

        GetComponentAuditListResponseDto GetContainerAudit(int limit, int page, PagedDataOperators operators);
    }
}
