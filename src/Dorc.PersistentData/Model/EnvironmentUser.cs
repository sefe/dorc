namespace Dorc.PersistentData.Model
{
    public class EnvironmentUser
    {
        public int DbId { get; set; }
        public int UserId { get; set; }
        public int PermissionId { get; set; }

        public Database Database { get; set; } = null!;
        public User User { get; set; } = null!;
        public Permission Permission { get; set; } = null!;
    }
}