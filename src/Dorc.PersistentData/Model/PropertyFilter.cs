using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class PropertyFilter
    {
        public int Id { get; set; }

        [StringLength(64)] public string Name { get; set; } = null!;

        public int Priority { get; set; }

        public string? Description { get; set; }
    }
}