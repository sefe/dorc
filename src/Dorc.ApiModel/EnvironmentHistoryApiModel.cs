namespace Dorc.ApiModel
{
    public class EnvironmentHistoryApiModel
    {
        public int Id { get; set; }
        public string EnvName { set; get; }
        public string UpdateDate { set; get; }
        public string UpdatedBy { set; get; }
        public string UpdateType { set; get; }
        public string OldVersion { set; get; }
        public string NewVersion { set; get; }
        public string TfsId { set; get; }
        public string Comment { set; get; }
    }
}