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
        public IList<string> ProjectNames { get; set; }
    }
}