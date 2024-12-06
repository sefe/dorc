using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    public class Database
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Type { get; set; }

        public string? ServerName { get; set; }

        public string? ArrayName { get; set; }
        public int? GroupId { get; set; }

        public virtual AdGroup Group { get; set; }

        public ICollection<Environment> Environments { get; set; } = null!;

        public ICollection<EnvironmentUser> EnvironmentUsers { get; set; } = null!;
    }
}