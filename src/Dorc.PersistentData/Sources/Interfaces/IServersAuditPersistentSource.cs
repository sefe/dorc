using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IServersAuditPersistentSource
    {
        void InsertServerAudit(string username, ActionType action, int? serverId, string? fromValue, string? toValue);
        GetServerAuditListResponseDto GetServerAudit(int limit, int page, PagedDataOperators operators);
    }
}
