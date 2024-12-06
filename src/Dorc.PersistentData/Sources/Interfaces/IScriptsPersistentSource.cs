using Dorc.ApiModel;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IScriptsPersistentSource
    {
        GetScriptsListResponseDto GetScriptsByPage(int limit, int page, PagedDataOperators operators);
        ScriptApiModel GetScript(int id);
        bool UpdateScript(ScriptApiModel script, IPrincipal user);
    }
}