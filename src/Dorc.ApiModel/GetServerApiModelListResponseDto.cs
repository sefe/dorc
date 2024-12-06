using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetServerApiModelListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<ServerApiModel> Items { get; set; }
    }
}
