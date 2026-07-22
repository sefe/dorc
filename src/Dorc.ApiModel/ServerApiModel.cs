using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class ServerApiModel : EnvironmentUiPartBase
    {
        public int ServerId { set; get; }
        public string Name { set; get; }
        public string OsName { set; get; }

        [StringLength(TagLimits.MaxTagStringLength,
            ErrorMessage = "Tags must be at most {1} characters (semicolon-separated).")]
        public string ApplicationTags { set; get; }
    }
}