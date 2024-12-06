namespace Dorc.PersistentData.Model
{
    public class RequestStatuses
    {
        public int Id { get; set; }
        public string? Components { get; set; }
        public DateTimeOffset? StartedTime { get; set; }
        public DateTimeOffset? CompletedTime { get; set; }
        public string Status { get; set; } = null!;
        public string? Log { get; set; }
        public bool IsProd { get; set; }
        public string? Project { get; set; }
        public string? EnvironmentName { get; set; }
        public string? BuildNumber { get; set; }
    }
}