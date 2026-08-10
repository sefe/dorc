using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    public class Database
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        /// <summary>
        /// DEPRECATED delimited column, dual-written until the follow-up release
        /// drops it. <see cref="TagLinks"/> is the source of truth.
        /// </summary>
        public string? Tags { get; set; }

        public ICollection<DatabaseTag> TagLinks { get; set; } = new List<DatabaseTag>();

        public string? ServerName { get; set; }

        public string? ArrayName { get; set; }
        public int? GroupId { get; set; }

        public virtual AdGroup Group { get; set; }

        public ICollection<Environment> Environments { get; set; } = null!;

        public ICollection<EnvironmentUser> EnvironmentUsers { get; set; } = null!;
    }
}