namespace Dorc.PersistentData.Model
{
    /// <summary>
    /// Common shape of the three environment-component audit entities
    /// (ContainerAudit, CloudResourceAudit, ApiRegistrationAudit), letting the
    /// paged audit read pipeline be shared. ComponentId is implemented
    /// explicitly on each entity so EF conventions never try to map it.
    /// </summary>
    public interface IComponentAudit
    {
        long Id { get; }

        int? ComponentId { get; }

        int RefDataAuditActionId { get; }

        RefDataAuditAction Action { get; }

        string Username { get; }

        DateTime Date { get; }

        string? FromValue { get; }

        string? ToValue { get; }
    }
}
