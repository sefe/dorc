using Dorc.ApiModel;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IScriptsPersistentSource
    {
        GetScriptsListResponseDto GetScriptsByPage(int limit, int page, PagedDataOperators operators);
        ScriptApiModel GetScript(int id);
        bool UpdateScript(ScriptApiModel script, IPrincipal user);

        /// <summary>
        /// Sets or clears the recorded content hash. A null or empty hash clears it, which means
        /// "re-record on next execution" rather than "stop verifying this one".
        /// </summary>
        bool RecordContentHash(int scriptId, string? contentHash, IPrincipal user);
    }
}