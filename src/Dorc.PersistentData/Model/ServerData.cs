namespace Dorc.PersistentData.Model
{
    public class ServerData
    {
        public Server Server { get; set; } = default!;
        public Environment Environment { get; set; } = default!;
        public bool UserEditable { get; set; }
    }
}
