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
    }

    public class CreateResponse
    {
        public string Message { get; set; }
        public int RequestId { get; set; }
    }
}