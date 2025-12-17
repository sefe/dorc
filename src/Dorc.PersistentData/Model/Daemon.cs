using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class Daemon
    {
        [Key] public virtual int Id { get; set; }

        [StringLength(50)] public virtual string Name { get; set; } = default!;

        [StringLength(50)] public virtual string DisplayName { get; set; } = default!;

        [StringLength(50)] public virtual string AccountName { get; set; } = default!;

        [StringLength(50)] public virtual string ServiceType { get; set; } = default!;

        public virtual ICollection<Server> Server { get; set; } = default!;
    }
}