namespace Dorc.PersistentData.Model
{
    public class DaemonObservation
    {
        public long Id { get; set; }

        public int ServerId { get; set; }

        public int DaemonId { get; set; }

        public DateTime ObservedAt { get; set; }

        public string? ObservedStatus { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
