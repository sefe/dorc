namespace Dorc.Core
{
    public class JSONPath
    {
        public string ScriptPath { get; set; } = string.Empty;

        public IEnumerable<IDictionary<string, string>> GenericArguments { get; set; } = Enumerable.Empty<IDictionary<string, string>>();
    }
}