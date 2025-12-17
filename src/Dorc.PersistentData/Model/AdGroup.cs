using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class AdGroup
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "You must supply an AD Group name")]
        public string Name { get; set; } = default!;

        public virtual ICollection<Database> Databases { get; set; } = new List<Database>();
    }
}