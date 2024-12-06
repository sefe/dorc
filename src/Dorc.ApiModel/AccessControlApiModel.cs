namespace Dorc.ApiModel
{
    public class AccessControlApiModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Sid { get; set; }
        public int Allow { get; set; }
        public int Deny { get; set; }
    }
}