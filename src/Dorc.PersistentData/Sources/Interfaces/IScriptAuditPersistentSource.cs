using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IScriptAuditPersistentSource
    {
        void InsertScriptAudit(string username, ActionType actionType, ScriptApiModel scriptApiModel);
    }
}