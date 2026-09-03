namespace Dorc.Core
{
    public class DaemonStatus
    {
        public string EnvName
        {
            get;
            set;
        }

        public string ServerName
        {
            get;
            set;
        }

        public string DaemonName
        {
            get;
            set;
        }

        public string Status
        {
            get;
            set;
        }

        public string? ErrorMessage
        {
            get;
            set;
        }

        /// <summary>
        /// Internal — populated by BuildDaemonList so ProbeDaemonStatuses can write observation rows
        /// keyed by ID. Not surfaced on the wire (DaemonStatusApiModel omits these).
        /// </summary>
        public int? ServerId
        {
            get;
            set;
        }

        public int? DaemonId
        {
            get;
            set;
        }
    }
}
