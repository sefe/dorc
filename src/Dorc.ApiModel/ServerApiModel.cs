using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class ServerApiModel : EnvironmentUiPartBase
    {
        public int ServerId { set; get; }
        public string Name { set; get; }
        public string OsName { set; get; }
        public string ApplicationTags { set; get; }
    }
}