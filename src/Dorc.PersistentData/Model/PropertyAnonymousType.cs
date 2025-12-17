namespace Dorc.PersistentData.Model
{
    public class PropertyAnonymousType
    {
        public virtual string InstallScriptParameterName { get; set; } = default!;
        public virtual int? PropertyId { get; set; }
        public virtual string PropertyName { get; set; } = default!;
        public virtual string PropertyType { get; set; } = default!;
        public virtual string PropertyDefaultValue { get; set; } = default!;
        public virtual bool Secure { get; set; }
    }
}