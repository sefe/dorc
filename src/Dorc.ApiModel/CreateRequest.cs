using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class CreateRequest
    {
        public string Project { get; set; }
        public string Environment { get; set; }
        public string BuildDefinitionName { get; set; }
        public string BuildUrl { get; set; }
        public string DropFolder { get; set; }
        public ICollection<string> Components { get; set; }
        public IEnumerable<RequestProperty> Properties { get; set; }
    }

    public class RequestProperty
    {
        public string PropertyName { get; set; }
        public string PropertyValue { get; set; }

        // structural signal that this property's value is sensitive and
        // must be redacted at every emission boundary (logs, API responses,
        // the request-history grid, audit records). The wizard's manifest-
        // driven path sets this to true for parameters whose
        // manifest declares Sensitive: true; existing callers (the deploy
        // page's per-deployment override UI) leave it false.
        // Additive: existing JSON payloads that omit the field deserialise to
        // false, preserving today's behaviour for non-Catalog flows.
        public bool IsSensitive { get; set; }
    }

    public class CreateResponse
    {
        public string Message { get; set; }
        public int RequestId { get; set; }
    }
}