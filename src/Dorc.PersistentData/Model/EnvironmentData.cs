namespace Dorc.PersistentData.Model
{
    public class EnvironmentData
    {
        public Environment Environment { get; set; }
        public bool UserEditable { get; set; }
        public bool IsOwner { get; set; }
        public bool IsModify { get; set; }
        public bool IsDelegate { get; set; }
    }

    public class EnvironmentDataComparer : IEqualityComparer<EnvironmentData>
    {
        public bool Equals(EnvironmentData? x, EnvironmentData? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Environment.Equals(y.Environment);
        }

        public int GetHashCode(EnvironmentData obj)
        {
            return obj.GetHashCode();
        }
    }
}
