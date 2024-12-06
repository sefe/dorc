namespace Dorc.ApiModel
{
    public class UserApiModel
    {
        public int Id { get; set; }
        public string LoginId { get; set; }
        public string DisplayName { get; set; }
        public string LoginType { get; set; }
        public string LanId { get; set; }
        public string Team { get; set; }

        public string LanIdType { get; set; }
    }
}