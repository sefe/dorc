using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetPropertyValuesAuditListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<PropertyValueAuditApiModel> Items { get; set; }
    }
}
