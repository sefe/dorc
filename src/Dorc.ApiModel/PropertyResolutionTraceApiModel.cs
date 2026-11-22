using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class PropertyResolutionTraceApiModel
    {
        public string PropertyName { get; set; } = string.Empty;
        public string RawValue { get; set; }
        public string ResolvedValueJson { get; set; }
        public string ValueType { get; set; } = "String";
        public string Source { get; set; } = "Unresolved";
        public string ScopeName { get; set; }
        public List<string> ResolutionChain { get; set; } = new List<string>();
        public bool IsSecure { get; set; }
        public string Diagnostic { get; set; } = "Ok";
    }
}
