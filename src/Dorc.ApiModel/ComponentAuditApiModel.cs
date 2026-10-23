namespace Dorc.ApiModel
{
    /// <summary>
    /// Audit row shared by the three environment component types
    /// (Container, CloudResource, ApiRegistration). ComponentId/ComponentName
    /// refer to whichever entity the serving endpoint covers.
    /// </summary>
    public class ComponentAuditApiModel
    {
        public long Id { get; set; }

        public int? ComponentId { get; set; }

        public string ComponentName { get; set; }

        public int RefDataAuditActionId { get; set; }

        public string Action { get; set; }

        public string Username { get; set; }

        public System.DateTime Date { get; set; }

        public string FromValue { get; set; }

        public string ToValue { get; set; }
    }
}
