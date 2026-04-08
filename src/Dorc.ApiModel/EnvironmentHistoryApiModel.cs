namespace Dorc.ApiModel
{
    public class EnvironmentHistoryApiModel
    {
        public int Id { get; set; }
        public string EnvName { set; get; }
        public string UpdateDate { set; get; }
        public string UpdatedBy { set; get; }
        public string UpdateType { set; get; }
        public string FromValue { set; get; }
        public string ToValue { set; get; }
        public string Details { set; get; }
        public string Comment { set; get; }
    }
}