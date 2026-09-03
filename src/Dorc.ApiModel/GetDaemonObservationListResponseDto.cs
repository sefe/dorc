using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetDaemonObservationListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<DaemonObservationApiModel> Items { get; set; }
    }
}
