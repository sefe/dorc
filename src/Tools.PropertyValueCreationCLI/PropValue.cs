namespace Tools.PropertyValueCreationCLI
{
    public class PropValue
    {
        public string PropertyName { get; set; }
        public string Value { get; set; }
        public string Environment { get; set; }
        public bool IsSecure { get; set; }


        public override string ToString()
        {
            return $"{PropertyName} - {Value} - {Environment} - {IsSecure}";
        }
    }
}