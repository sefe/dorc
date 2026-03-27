namespace Dorc.ApiModel
{
    public class ApiConfigModel
    {
        public string AuthenticationScheme { get; set; }
        public string OAuthAuthority { get; set; }
        public string OAuthUiClientId { get; set; }
        public string OAuthUiRequestedScopes { get; set; }
        public bool PauseDeploymentEnabled { get; set; }
        public bool IsProduction { get; set; }
    }
}