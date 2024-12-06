namespace Dorc.PersistentData.Model
{
    public class Audit
    {
        public long Id { get; set; }

        public long? PropertyId { get; set; }

        public long? PropertyValueId { get; set; }

        public string? PropertyName { get; set; }

        public string? EnvironmentName { get; set; }

        public string? FromValue { get; set; }

        public string? ToValue { get; set; }

        public string? UpdatedBy { get; set; }

        public DateTime UpdatedDate { get; set; }

        public string? Type { get; set; }
    }
}