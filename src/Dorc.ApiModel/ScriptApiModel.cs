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
    }
}