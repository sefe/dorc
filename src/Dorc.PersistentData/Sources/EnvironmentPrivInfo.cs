using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentPrivInfo
    {
        public Environment Environment { get; set; }
        public bool HasPermission { get; set; }
        public bool IsDelegate { get; set; }
        public bool IsOwner { get; set; }
    }
}