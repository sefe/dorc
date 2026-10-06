namespace Dorc.ApiModel
{
    public class UpdateEnvironmentHistoryRequest
    {
        public string EnvName { get; set; } = string.Empty;
        public string BackupFile { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
        public string UpdateType { get; set; } = string.Empty;
    }
}