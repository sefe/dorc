using Dorc.ApiModel;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Dorc.Monitor.Notifications.Teams
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
            var emoji = StatusEmoji(finalStatus);

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

        /// <summary>
        /// Card for a set of requests that reached the same status together, so a bulk sweep
        /// sends one message per requester instead of one per request.
        /// </summary>
        public string BuildBatch(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            string finalStatus,
            DateTimeOffset completedTime)
        {
            if (requests.Count == 0)
                throw new ArgumentException("A batch card needs at least one request.", nameof(requests));

            var emoji = StatusEmoji(finalStatus);
            var ordered = requests.OrderBy(r => r.Id).ToList();

            // Keep the card readable: list the first few and summarise the rest.
            const int MaxListed = 10;
            var listed = ordered.Take(MaxListed).ToList();
            var remaining = ordered.Count - listed.Count;

            var lines = listed.Select(r =>
            {
                var where = string.Join(" / ", new[] { r.Project, r.EnvironmentName }
                    .Where(v => !string.IsNullOrWhiteSpace(v)));
                return string.IsNullOrEmpty(where) ? $"- #{r.Id}" : $"- #{r.Id} — {where}";
            }).ToList();

            if (remaining > 0)
                lines.Add($"- …and {remaining} more");

            var body = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["type"]   = "TextBlock",
                    ["text"]   = $"{emoji} {ordered.Count} deployments {finalStatus}",
                    ["weight"] = "Bolder",
                    ["size"]   = "Medium",
                    ["wrap"]   = true
                },
                new Dictionary<string, object?>
                {
                    ["type"]  = "FactSet",
                    ["facts"] = new object[]
                    {
                        new Dictionary<string, object?> { ["title"] = "Requester", ["value"] = ordered[0].UserName ?? "—" },
                        new Dictionary<string, object?> { ["title"] = "Status",    ["value"] = finalStatus },
                        new Dictionary<string, object?> { ["title"] = "Requests",  ["value"] = ordered.Count.ToString() },
                        new Dictionary<string, object?> { ["title"] = "At",        ["value"] = completedTime.ToString("yyyy-MM-dd HH:mm:ss zzz") }
                    }
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "TextBlock",
                    ["text"] = string.Join("\r", lines),
                    ["wrap"] = true
                }
            };

            var card = new Dictionary<string, object?>
            {
                ["type"]         = "AdaptiveCard",
                ["version"]      = "1.4",
                ["fallbackText"] = $"{emoji} {ordered.Count} deployments {finalStatus}",
                ["body"]         = body,
                ["$schema"]      = "http://adaptivecards.io/schemas/adaptive-card.json"
            };

            if (!string.IsNullOrWhiteSpace(_dorcUiBaseUrl))
            {
                card["actions"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"]  = "Action.OpenUrl",
                        ["title"] = "Open in DOrc",
                        ["url"]   = $"{_dorcUiBaseUrl.TrimEnd('/')}/monitor-requests"
                    }
                };
            }

            return JsonSerializer.Serialize(card);
        }

        private static string StatusEmoji(string finalStatus) => finalStatus switch
        {
            "Completed"           => "✅",
            "Failed"              => "❌",
            "Errored"             => "🔴",
            "WaitingConfirmation" => "⏳",
            "Cancelled"           => "🚫",
            _                     => "ℹ️"
        };
    }
}