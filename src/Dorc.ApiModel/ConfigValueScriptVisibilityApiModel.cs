using System.Text.Json.Serialization;

namespace Dorc.ApiModel
{
    public class ConfigValueScriptVisibilityApiModel
    {
        [JsonRequired]
        public bool VisibleToScripts { get; set; }
    }
}
