using System;

namespace Dorc.ApiModel
{
    public class DatabaseApiModel : EnvironmentUiPartBase
    {
        public string Name { set; get; }
        public string Type { set; get; }
        public string ServerName { set; get; }
        public string AdGroup { set; get; }
        public int Id { set; get; }
        public string ArrayName { set; get; }
        public DateTime? LastChecked { set; get; }
        public bool? IsReachable { set; get; }
    }
}