namespace Dorc.Monitor.Notifications.Teams
{
    internal sealed class TeamsBotOptions
    {
        public const string SectionName = "TeamsNotification";

        public bool Enabled { get; init; }
        public string BotAppId { get; init; } = string.Empty;
        public string BotAppPassword { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public string ServiceUrl { get; init; } = "https://smba.trafficmanager.net/uk/";
        public string DorcUiBaseUrl { get; init; } = string.Empty;
    }
}