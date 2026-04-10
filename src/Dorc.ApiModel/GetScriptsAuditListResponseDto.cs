using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetScriptsAuditListResponseDto
    {
        public int CurrentPage { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public List<ScriptAuditApiModel> Items { get; set; }
    }
}