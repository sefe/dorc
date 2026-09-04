namespace Dorc.PersistentData.Model
{
    /// <summary>
    /// One tag on one server. See <see cref="DatabaseTag"/> for why tags are rows.
    /// </summary>
    public class ServerTag
    {
        public int ServerId { get; set; }

        public string Tag { get; set; } = null!;

        public virtual Server Server { get; set; } = null!;
    }
}
