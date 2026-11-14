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
        /// Atomically records the candidate only while the script is still unrecorded and still
        /// targets <paramref name="expectedPath"/>, preventing a queued snapshot from pinning an
        /// older file's hash onto a live script row whose path was edited after submission.
        /// </summary>
        string? RecordContentHashIfUnrecorded(int scriptId, string expectedPath, string contentHash, IPrincipal user);

        /// <summary>
        /// Sets or clears the recorded content hash. A null or empty hash clears it, which means
        /// "re-record on next execution" rather than "stop verifying this one". This is the
        /// administrative operation and may deliberately replace an existing baseline.
        /// </summary>
        bool RecordContentHash(int scriptId, string? contentHash, IPrincipal user);

        /// <summary>
        /// Atomically verifies referencing-project authorization and optional <paramref name="expectedPath"/>,
        /// updates the recorded content hash, and appends the corresponding script audit entry in a
        /// single database transaction.
        /// </summary>
        bool RecordContentHash(
            int scriptId,
            string? contentHash,
            string? expectedPath,
            IPrincipal user,
            Func<int, bool>? projectAuthorize = null);
    }
}