using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class Daemon
    {
        [Key] public virtual int Id { get; set; }

        [StringLength(50)] public virtual string Name { get; set; }

        [StringLength(50)] public virtual string DisplayName { get; set; }

        [StringLength(50)] public virtual string AccountName { get; set; }

        [StringLength(50)] public virtual string ServiceType { get; set; }

        public virtual ICollection<Server> Server { get; set; }
    }
}