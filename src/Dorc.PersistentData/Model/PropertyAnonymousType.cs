namespace Dorc.PersistentData.Model
{
    public class PropertyAnonymousType
    {
        public virtual string InstallScriptParameterName { get; set; }
        public virtual int? PropertyId { get; set; }
        public virtual string PropertyName { get; set; }
        public virtual string PropertyType { get; set; }
        public virtual string PropertyDefaultValue { get; set; }
        public virtual bool Secure { get; set; }
    }
}