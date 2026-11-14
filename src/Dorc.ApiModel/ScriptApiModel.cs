using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class ScriptApiModel
    {
        public string Revision { get; set; }
        public int[] AffectedProjectIds { get; set; }
        public bool ConfirmImpact { get; set; }
        public bool CanEdit { get; set; }
        public virtual int Id { get; set; }
        public virtual string Name { get; set; }
        public virtual string Path { get; set; }
        public virtual bool IsPathJSON { get; set; }
        public virtual bool NonProdOnly { get; set; }
        public string InstallScriptName { get; set; }
        public bool IsEnabled { set; get; }
        public string PowerShellVersionNumber { set; get; }

        /// <summary>
        /// SHA-256, lower-case 64-character hex, over the UTF-8 bytes of the decoded script text
        /// the runner is expected to execute, or null when no hash has been recorded.
        /// </summary>
        /// <remarks>
        /// Not settable through the ordinary script edit. It is recorded by the deployment path
        /// on first use and set deliberately by a promotion pipeline or administrator; changing
        /// <see cref="Path"/> clears any recorded baseline automatically.
        /// </remarks>
        public string ContentHash { set; get; }

        public IList<string> ProjectNames { get; set; }
    }
}