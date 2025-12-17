namespace Dorc.PersistentData.Model
{
    public class PropertyValueEnv
    {
        public virtual long PropertyValueId { get; set; }

        public virtual string PropertyValue { get; set; } = default!;

        public virtual string EnvironmentName { get; set; } = default!;
    }
}