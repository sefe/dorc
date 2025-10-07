namespace Dorc.ApiModel
{
    public class UserDbPermissionApiModel
    {
        public int UserId { get; set; }
        public string UserLoginId { get; set; }
        public string UserLoginType { get; set; }
        public int PermissionId { get; set; }
        public string PermissionName { get; set; }
        public string PermissionDisplayName { get; set; }
        public int DbId { get; set; }
        public string DbType { get; set; }
    }
}
