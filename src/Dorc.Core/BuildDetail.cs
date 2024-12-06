namespace Dorc.Core
{
    [Serializable]
    public class BuildDetail : ICloneable
    {
        public int BuildId { get; set; }
        public string ProjectUrl { get; set; } = String.Empty;
        public string Project { get; set; } = String.Empty;
        public string BuildNumber { get; set; } = String.Empty;
        public string DropLocation { get; set; } = String.Empty;
        public string Uri { get; set; } = String.Empty;

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}