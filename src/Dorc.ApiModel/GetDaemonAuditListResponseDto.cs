using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetDaemonAuditListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<DaemonAuditApiModel> Items { get; set; }
    }
}
