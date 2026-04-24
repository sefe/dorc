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
    }
}
