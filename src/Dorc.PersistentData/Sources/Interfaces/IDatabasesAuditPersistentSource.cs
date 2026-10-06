using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDatabasesAuditPersistentSource
    {
        void InsertDatabaseAudit(string username, ActionType action, int? databaseId, string? fromValue, string? toValue);
        GetDatabaseAuditListResponseDto GetDatabaseAudit(int limit, int page, PagedDataOperators operators);
    }
}
