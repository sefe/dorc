using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dorc.PersistentData.Model
{
    public class DatabaseAudit
    {
        public long Id { get; set; }

        public int? DatabaseId { get; set; }

        public int RefDataAuditActionId { get; set; }

        public RefDataAuditAction Action { get; set; } = null!;

        public string Username { get; set; } = null!;

        public DateTime Date { get; set; }

        public string? FromValue { get; set; }

        public string? ToValue { get; set; }
    }
}
