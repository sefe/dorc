using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class ServerApiModel : EnvironmentUiPartBase
    {
        public int ServerId { set; get; }
        public string Name { set; get; }
        public string OsName { set; get; }

        /// <summary>
        /// Tags carried by this server. Stored as rows in deploy.ServerTag; each
        /// entry is at most <see cref="TagLimits.MaxTagLength"/> characters.
        /// </summary>
        [TagSet]
        public string[] Tags { set; get; } = System.Array.Empty<string>();
    }
}
