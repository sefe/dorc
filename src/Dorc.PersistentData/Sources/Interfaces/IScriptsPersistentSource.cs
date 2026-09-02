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
        /// Atomically records the candidate only while the script is still unrecorded, then
        /// returns the authoritative stored hash. If another deployment records first, its hash
        /// is returned instead of replacing it.
        /// </summary>
        string? RecordContentHashIfUnrecorded(int scriptId, string contentHash, IPrincipal user);

        /// <summary>
        /// Sets or clears the recorded content hash. A null or empty hash clears it, which means
        /// "re-record on next execution" rather than "stop verifying this one". This is the
        /// administrative operation and may deliberately replace an existing baseline.
        /// </summary>
        bool RecordContentHash(int scriptId, string? contentHash, IPrincipal user);
    }
}