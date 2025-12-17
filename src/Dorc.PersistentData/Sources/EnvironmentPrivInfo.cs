using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentPrivInfo
    {
        public Environment Environment { get; set; } = default!;
        public bool HasPermission { get; set; }
        public bool IsDelegate { get; set; }
        public bool IsOwner { get; set; }
    }
}