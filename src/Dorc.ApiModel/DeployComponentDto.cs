namespace Dorc.ApiModel
{
    public class DeployComponentDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int? ParentId { get; set; }
        public int? NumOfChildren { get; set; }
        public bool? IsEnabled { set; get; }
    }
}
