using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class DiscoverDaemonsResult
    {
        public int MappingsCreated { get; set; }
        public int ServersProcessed { get; set; }
        public int DaemonsDiscovered { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public bool Success { get; set; }

        /// <summary>
        /// List of discovered daemon statuses to display in UI
        /// </summary>
        public List<DaemonStatusApiModel> DiscoveredDaemons { get; set; } = new List<DaemonStatusApiModel>();
    }
}