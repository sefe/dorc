using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace Dorc.PersistentData.Model
{
    public class DeploymentRequest
    {
        private XElement _xml;

        public DeploymentRequest()
        {
            Status = "Pending";
        }

        public int Id { get; set; }
        public string RequestDetails { get; set; }

        [StringLength(128)] public string UserName { get; set; }

        public DateTimeOffset? RequestedTime { get; set; }

        public DateTimeOffset? StartedTime { get; set; }

        public DateTimeOffset? CompletedTime { get; set; }

        public string Status { get; set; }

        public ICollection<DeploymentResult>? DeploymentResults { get; set; }

        public ICollection<DeploymentRequestProcess>? DeploymentRequestProcesses { get; set; }

        public string? Log { get; set; }

        public string? Project { get; set; }

        public string? Environment { get; set; }

        public string? BuildNumber { get; set; }

        public string? Components { get; set; }
        public string? UncLogPath { get; set; }

        public string DropLocation
        {
            get
            {
                var element = Xml.Descendants("DropLocation").SingleOrDefault();
                return element?.Value;
            }
        }

        public string BuildUri
        {
            get
            {
                var element = Xml.Descendants("Uri").SingleOrDefault();
                return element?.Value;
            }
        }

        private XElement Xml
        {
            get
            {
                _xml = XElement.Parse(RequestDetails);

                return _xml;
            }
        }

        public bool IsProd { get; set; }
    }
}