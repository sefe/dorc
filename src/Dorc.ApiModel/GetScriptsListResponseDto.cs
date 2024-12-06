using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetScriptsListResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<ScriptApiModel> Items { get; set; }
    }
}
