namespace Dorc.Core
{
    public class JSONPath
    {
        public string ScriptPath { get; set; }

        public IEnumerable<IDictionary<string, string>> GenericArguments { get; set; }
    }
}