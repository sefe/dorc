using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetRefDataAuditListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<RefDataAuditApiModel> Items { get; set; }
    }
}
