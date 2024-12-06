using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetRequestStatusesListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<DeploymentRequestApiModel> Items { get; set; }
    }
}
