namespace Dorc.PersistentData.Model
{
    public class Environment : SecurityObject
    {
        public int Id { get; set; }
        public bool Secure { get; set; }
        public bool IsProd { get; set; }
        public string? Owner { get; set; }
        public string? ThinClientServer { get; set; }
        public string? RestoredFromBackup { get; set; }
        public DateTime? LastUpdate { get; set; }
        public string? FileShare { get; set; }
        public string? EnvNote { get; set; }
        public string? Description { get; set; }
        public int? ParentId { get; set; }

        public ICollection<Database> Databases { get; set; } = new List<Database>();
        public ICollection<EnvironmentHistory> Histories { get; set; } = new List<EnvironmentHistory>();
        public ICollection<Server> Servers { get; set; } = new List<Server>();
        public ICollection<User> Users { set; get; } = new List<User>();
        public ICollection<EnvironmentComponentStatus> ComponentStatus { get; set; } = new List<EnvironmentComponentStatus>();
        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public Environment? ParentEnvironment { get; set; } = null;
        public ICollection<Environment> ChildEnvironments { get; set; } = new List<Environment>();
        public ICollection<AccessControl> AccessControls { get; set; } = new List<AccessControl>();
    }

    public class EnvironmentComparer : IEqualityComparer<Environment>
    {
        public bool Equals(Environment? x, Environment? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Id == y.Id;
        }

        public int GetHashCode(Environment obj)
        {
            return obj.GetHashCode();
        }
    }
}