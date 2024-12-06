using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetDatabaseApiModelListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<DatabaseApiModel> Items { get; set; }
    }
}
