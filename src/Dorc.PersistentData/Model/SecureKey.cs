namespace Dorc.PersistentData.Model
{
    public class SecureKey
    {
        public virtual int Id { get; set; }
        public virtual string IV { get; set; } = default!;
        public virtual string Key { get; set; } = default!;
    }
}