using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseApiModel : EnvironmentUiPartBase
    {
        public string Name { set; get; }

        [StringLength(TagLimits.MaxTagStringLength,
            ErrorMessage = "Tags must be at most {1} characters (semicolon-separated).")]
        public string Type { set; get; }

        public string ServerName { set; get; }
        public string AdGroup { set; get; }
        public int Id { set; get; }
        public string ArrayName { set; get; }
    }
}
