using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IScriptsAuditPersistentSource
    {
        void AddRecord(long scriptId, string scriptName, string fromValue, string toValue,
            string updatedBy, string type, string projectNames);

        GetScriptsAuditListResponseDto GetScriptAuditsByPage(int limit, int page,
            PagedDataOperators operators, bool useAndLogic);
    }
}