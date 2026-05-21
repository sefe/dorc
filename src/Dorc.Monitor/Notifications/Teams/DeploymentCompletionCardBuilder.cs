using Dorc.ApiModel;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Dorc.Monitor.Teams
{
    internal sealed class DeploymentCompletionCardBuilder
    {
        private readonly string _dorcUiBaseUrl;

        public DeploymentCompletionCardBuilder(IOptions<TeamsBotOptions> options)
        {
            _dorcUiBaseUrl = options.Value.DorcUiBaseUrl;
        }

        public string Build(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            var emoji = finalStatus switch
            {
                "Completed"           => "✅",
                "Failed"              => "❌",
                "Errored"             => "🔴",
                "WaitingConfirmation" => "⏳",
                "Cancelled"           => "🚫",
                _                     => "ℹ️"
            };

            var duration     = completedTime - startedTime;
            var durationText = duration.TotalSeconds < 60
                ? $"{(int)duration.TotalSeconds}s"
                : $"{(int)duration.TotalMinutes}m {duration.Seconds}s";

            var card = new Dictionary<string, object?>
            {
                ["type"]         = "AdaptiveCard",
                ["version"]      = "1.4",
                ["fallbackText"] = $"{emoji} Deployment {finalStatus} - Request #{request.Id}",
                ["body"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"]   = "TextBlock",
                        ["text"]   = $"{emoji} Deployment {finalStatus}",
                        ["weight"] = "Bolder",
                        ["size"]   = "Medium",
                        ["wrap"]   = true
                    },
                    new Dictionary<string, object?>
                    {
                        ["type"]  = "FactSet",
                        ["facts"] = new object[]
                        {
                            new Dictionary<string, object?> { ["title"] = "Request ID",  ["value"] = request.Id.ToString() },
                            new Dictionary<string, object?> { ["title"] = "Requester",   ["value"] = request.UserName ?? "—" },
                            new Dictionary<string, object?> { ["title"] = "Project",     ["value"] = request.Project ?? "—" },
                            new Dictionary<string, object?> { ["title"] = "Environment", ["value"] = request.EnvironmentName ?? "—" },
                            new Dictionary<string, object?> { ["title"] = "Build",       ["value"] = request.BuildNumber ?? "—" },
                            new Dictionary<string, object?> { ["title"] = "Status",      ["value"] = finalStatus },
                            new Dictionary<string, object?> { ["title"] = "Started",     ["value"] = startedTime.ToString("yyyy-MM-dd HH:mm:ss zzz") },
                            new Dictionary<string, object?> { ["title"] = "Completed",   ["value"] = completedTime.ToString("yyyy-MM-dd HH:mm:ss zzz") },
                            new Dictionary<string, object?> { ["title"] = "Duration",    ["value"] = durationText }
                        }
                    }
                },
                ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json"
            };

            if (!string.IsNullOrWhiteSpace(_dorcUiBaseUrl))
            {
                var deepLinkUrl = $"{_dorcUiBaseUrl.TrimEnd('/')}/monitor-result/{request.Id}";
                card["actions"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"]  = "Action.OpenUrl",
                        ["title"] = "Open in DOrc",
                        ["url"]   = deepLinkUrl
                    }
                };
            }

            return JsonSerializer.Serialize(card);
        }
    }
}