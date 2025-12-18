namespace Dorc.Core
{
    [Serializable]
    public class DeploymentRequestDetail : ICloneable
    {
        public DeploymentRequestDetail()
        {
            Properties = new List<PropertyPair>();
            ComponentsToSkip = new List<string>();
        }

        public string EnvironmentName { get; set; } = string.Empty;
        public List<string> Components { get; set; } = new();
        public List<string> ComponentsToSkip { get; set; }
        public BuildDetail BuildDetail { get; set; } = new();
        public List<PropertyPair> Properties { get; set; }

        public object Clone()
        {
            var other = new DeploymentRequestDetail
            {
                EnvironmentName = EnvironmentName,
                Components = new List<string>(Components),
                ComponentsToSkip = new List<string>(ComponentsToSkip),
                BuildDetail = (BuildDetail)BuildDetail.Clone(),
                Properties = new List<PropertyPair>(Properties.Select(x => (PropertyPair)x.Clone()))
            };

            return other;
        }
    }

    [Serializable]
    public class PropertyPair : ICloneable
    {
        public PropertyPair()
        {
        }

        public PropertyPair(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; set; } = String.Empty;
        public string Value { get; set; } = String.Empty;

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
    [Serializable]
    public class BuildBundle
    {
        public IEnumerable<BuildItem> Items { set; get; } = new List<BuildItem>();
    }
    [Serializable]
    public class BuildItem
    {
        public string BuildDefinition { set; get; } = String.Empty;
        public string Component { set; get; } = String.Empty;
        public string Build { set; get; } = String.Empty;
    }
}