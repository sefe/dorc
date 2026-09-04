using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseApiModel : EnvironmentUiPartBase
    {
        public string Name { set; get; }

        /// <summary>
        /// Tags carried by this database. Stored as rows in deploy.DatabaseTag;
        /// each entry is at most <see cref="TagLimits.MaxTagLength"/> characters.
        /// </summary>
        [TagSet]
        public string[] Tags { set; get; } = System.Array.Empty<string>();

        public string ServerName { set; get; }
        public string AdGroup { set; get; }
        public int Id { set; get; }
        public string ArrayName { set; get; }
    }
}
