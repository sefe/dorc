using Microsoft.Extensions.Configuration;

namespace Dorc.Kafka.Client.Configuration;

/// <summary>
/// Single source of truth for the host-startup Kafka enable/fallback gate shared
/// by the Monitor and API hosts (audit CR#10). Previously each host re-implemented
/// the same two-condition check; a future required precondition had to be edited
/// in both <c>Program.cs</c> files, and editing only one would let the other host
/// start with Kafka enabled but missing config — the opaque DI-resolution crash
/// this guard exists to prevent.
/// </summary>
public static class KafkaStartupGate
{
    /// <summary>
    /// Required Kafka configuration keys whose absence triggers graceful fallback.
    /// </summary>
    private static readonly (string Key, string Why)[] RequiredKeys =
    {
        ("Kafka:BootstrapServers", "no brokers configured"),
        ("Kafka:SchemaRegistry:Url", "Avro serialisation requires the schema registry URL"),
    };

    /// <summary>
    /// Returns whether Kafka should be enabled at startup. <c>Kafka:Enabled</c>
    /// defaults to true (migration intent). When enabled but a required key is
    /// missing, returns false and reports the gap via <paramref name="reportFallback"/>
    /// — upgrade-safety so an existing install that picked up the new Kafka section
    /// without full config falls back cleanly instead of crashing at DI resolution.
    /// </summary>
    /// <param name="configuration">Host configuration (use the one that honours env/CLI overrides).</param>
    /// <param name="fallbackModeLabel">Human label for the degraded mode, e.g. "DB-poll fallback mode".</param>
    /// <param name="reportFallback">Sink for the one-line fallback message (e.g. <see cref="System.Console.WriteLine(string)"/>).</param>
    public static bool IsKafkaEnabled(
        IConfiguration configuration,
        string fallbackModeLabel,
        Action<string> reportFallback)
    {
        if (!configuration.GetValue("Kafka:Enabled", true))
            return false;

        foreach (var (key, why) in RequiredKeys)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
            {
                reportFallback(
                    $"[startup] Kafka:Enabled=true but {key} is empty ({why}); running in {fallbackModeLabel}.");
                return false;
            }
        }

        return true;
    }
}
