using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class ScriptApiModel
    {
        public virtual int Id { get; set; }
        public virtual string Name { get; set; }
        public virtual string Path { get; set; }
        public virtual bool IsPathJSON { get; set; }
        public virtual bool NonProdOnly { get; set; }
        public string InstallScriptName { get; set; }
        public bool IsEnabled { set; get; }
        public string PowerShellVersionNumber { set; get; }

        /// <summary>
        /// SHA-256, lower-case hex, of the bytes the runner is expected to execute, or null when
        /// no hash has been recorded.
        /// </summary>
        /// <remarks>
        /// Not settable through the ordinary script edit. It is recorded by the deployment path
        /// on first use and set deliberately by a promotion pipeline, so folding it into the
        /// general update would let an edit of a script's NAME silently re-baseline what that
        /// script is allowed to be.
        /// </remarks>
        public string ContentHash { set; get; }

        public IList<string> ProjectNames { get; set; }
    }
}