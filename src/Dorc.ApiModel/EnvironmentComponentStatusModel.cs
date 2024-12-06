using System;

namespace Dorc.ApiModel
{
    public class EnvironmentComponentStatusModel
    {
        public string Component { get; set; }
        public string Status { get; set; }
        public DateTimeOffset UpdateDate { get; set; }
        public string BuildNumber { get; set; }
        public string DropLocation { get; set; }
        public string Uri { get; set; }
        public string Project { get; set; }
    }
}