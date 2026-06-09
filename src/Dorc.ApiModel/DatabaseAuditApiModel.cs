using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class DatabaseAuditApiModel
    {
        public long Id { get; set; }

        public int? DatabaseId { get; set; }

        public string DatabaseName { get; set; }

        public int RefDataAuditActionId { get; set; }

        public string Action { get; set; }

        public string Username { get; set; }

        public System.DateTime Date { get; set; }

        public string FromValue { get; set; }

        public string ToValue { get; set; }

    }
}
