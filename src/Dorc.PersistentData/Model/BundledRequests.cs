using Dorc.ApiModel;

namespace Dorc.PersistentData.Model
{
    public class BundledRequests
    {
        public int Id { get; set; }

        public string? BundleName { get; set; }

        public int? ProjectId { get; set; }

        public BundledRequestType Type { get; set; }

        public string? RequestName { get; set; }

        public int? Sequence { get; set; }

        public string? Request { get; set; }

    }
}