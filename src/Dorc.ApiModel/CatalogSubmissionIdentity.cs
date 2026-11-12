using System;

namespace Dorc.ApiModel
{
    // Server-internal context; never bind this from a request body or persist CanonicalPayload.
    public class CatalogSubmissionIdentity
    {
        public string Caller { get; set; }
        public Guid Key { get; set; }
        public string CanonicalPayload { get; set; }
        public string ComponentJson { get; set; }
    }
}
