namespace Dorc.ApiModel
{
    public class DaemonObservationApiModel
    {
        public long Id { get; set; }

        public int ServerId { get; set; }

        public string ServerName { get; set; }

        public int DaemonId { get; set; }

        public System.DateTime ObservedAt { get; set; }

        public string ObservedStatus { get; set; }

        public string ErrorMessage { get; set; }
    }
}
