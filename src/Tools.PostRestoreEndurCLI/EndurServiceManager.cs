using System;

namespace Tools.PostRestoreEndurCLI
{
    public class EndurServiceManager
    {
        public int id { get; set; }
        public string service_name { get; set; }
        public string login_name { get; set; }
        public string workstation_name { get; set; }
        public int default_service { get; set; }
        public string app_login_name { get; set; }
        public DateTime curr_date { get; set; }
        public int user_id { get; set; }
    }
}