namespace Dorc.PersistentData.Model
{
    public class ContainerAudit : IComponentAudit
    {
        public long Id { get; set; }

        public int? ContainerId { get; set; }

        // Explicit implementation so EF model conventions don't map it.
        int? IComponentAudit.ComponentId => ContainerId;

        public int RefDataAuditActionId { get; set; }

        public RefDataAuditAction Action { get; set; } = null!;

        public string Username { get; set; } = null!;

        public DateTime Date { get; set; }

        public string? FromValue { get; set; }

        public string? ToValue { get; set; }
    }
}
