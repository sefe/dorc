namespace Dorc.ApiModel
{
    public class BundledRequestsApiModel
    {
        public int Id { get; set; }

        public string BundleName { get; set; }

        public long? ProjectId { get; set; }

        public BundledRequestType Type { get; set; }

        public string RequestName { get; set; }

        public int? Sequence { get; set; }

        public string Request { get; set; }

    }
}
