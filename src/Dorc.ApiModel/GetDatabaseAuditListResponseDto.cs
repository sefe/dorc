using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class GetDatabaseAuditListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<DatabaseAuditApiModel> Items { get; set; }
    }
}
