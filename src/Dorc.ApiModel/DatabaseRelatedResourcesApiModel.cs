using System.Collections.Generic;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Environments attached to a database and the servers that belong to those environments.
    /// </summary>
    public class DatabaseRelatedResourcesApiModel
    {
        public int DatabaseId { get; set; }
        public string DatabaseName { get; set; }
        public string ServerName { get; set; }
        public IList<EnvironmentShortApiModel> Environments { get; set; } = new List<EnvironmentShortApiModel>();
        public IList<ServerApiModel> Servers { get; set; } = new List<ServerApiModel>();
    }
}