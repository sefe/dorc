using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDaemonAuditPersistentSource
    {
        void InsertDaemonAudit(string username, ActionType action, int? daemonId, string? fromValue, string? toValue);

        GetDaemonAuditListResponseDto GetDaemonAuditByDaemonId(int daemonId, int limit, int page, PagedDataOperators operators);
    }
}
