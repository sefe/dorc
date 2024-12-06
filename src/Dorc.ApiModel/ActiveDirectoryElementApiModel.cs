namespace Dorc.ApiModel
{
    public class ActiveDirectoryElementApiModel
    {
        public string DisplayName { get; set; }
        public string Sid { get; set; }
        public string Username { get; set; }
        public bool IsGroup { get; set; }
        public string Email { get; set; }
    }
}
