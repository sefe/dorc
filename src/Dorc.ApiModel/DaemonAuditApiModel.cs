namespace Dorc.ApiModel
{
    public class DaemonAuditApiModel
    {
        public long Id { get; set; }

        public int? DaemonId { get; set; }

        public int RefDataAuditActionId { get; set; }

        public string Action { get; set; }

        public string Username { get; set; }

        public System.DateTime Date { get; set; }

        public string FromValue { get; set; }

        public string ToValue { get; set; }
    }
}
