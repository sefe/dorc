namespace Dorc.PersistentData.Model
{
    public class EnvironmentData
    {
        public Environment Environment { get; set; } = default!;
        public bool UserEditable { get; set; }
        public bool IsOwner { get; set; }
        public bool IsModify { get; set; }
        public bool IsDelegate { get; set; }
    }
}
