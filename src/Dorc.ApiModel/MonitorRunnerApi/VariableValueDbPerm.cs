namespace Dorc.ApiModel.MonitorRunnerApi
{
    public class DatabaseDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;

                hash += 23 * Name.GetHashCode();
                hash += 23 * Type.GetHashCode();

                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as DatabaseDefinition;
            if (other == null) return false;

            return Name == other.Name && Type == other.Type;
        }
    }
    public class DbUserRole
    {
        public string User { get; }
        public string[] Roles { get; }

        public DbUserRole(string user, string[] roles)
        {
            User = user;
            Roles = roles;
        }
    }

    public class VariableValueDbPerm
    {
        public DatabaseDefinition Database { get; set; }
        public DbUserRole[] Users { get; set; }
    }
}
