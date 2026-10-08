using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetComponentAuditListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<ComponentAuditApiModel> Items { get; set; }
    }
}
