namespace Dorc.Core.Models
{
    public class DeployableComponent
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public int? NumOfChildren { get; set; }
        public bool IsEnabled { set; get; }
    }
}
