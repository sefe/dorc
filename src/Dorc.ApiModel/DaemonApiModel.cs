namespace Dorc.ApiModel
{
    /// <summary>
    ///     Represent Service Model
    /// </summary>
    public class DaemonApiModel
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string AccountName { get; set; }

        public string ServiceType { get; set; }
        public string ServerName { get; set; }
    }
}