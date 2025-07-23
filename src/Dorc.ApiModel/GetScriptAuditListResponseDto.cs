using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetScriptAuditListResponseDto
    {
        public List<ScriptAuditApiModel> Items { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public long TotalItems { get; set; }
    }
}