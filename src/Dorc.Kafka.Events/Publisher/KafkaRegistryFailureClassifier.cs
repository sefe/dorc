using System.Net.Http;
using Confluent.Kafka;
using Confluent.SchemaRegistry;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Single source of truth for classifying a consume-time
/// <see cref="ConsumeException"/> as a TRANSIENT schema-registry / network
/// failure (back off and retry, leaving the offset un-advanced) versus a
/// deterministic poison message (route to the failure / DLQ path). Shared by
/// both <see cref="DeploymentRequestsKafkaConsumer"/> and
/// <see cref="DeploymentResultsKafkaConsumer"/> so the classification can never
/// drift between them (audit CR#9).
/// </summary>
internal static class KafkaRegistryFailureClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> when the failure is transient and the
    /// record should be retried rather than discarded.
    /// <para>
    /// <see cref="SchemaRegistryException"/> IS-A <see cref="HttpRequestException"/>,
    /// so it must be checked first; otherwise every registry HTTP error (including
    /// 4xx poison conditions) would be misclassified as a network failure.
    /// </para>
    /// Transient status codes:
    /// <list type="bullet">
    ///   <item>5xx — server-side error.</item>
    ///   <item>429 Too Many Requests — registry throttling under load (audit CR#2):
    ///   the message is valid and will deserialise once the throttle clears, so
    ///   discarding it would drop a real deployment event.</item>
    ///   <item>408 Request Timeout — the registry didn't answer in time; also a
    ///   retryable condition rather than a bad message.</item>
    /// </list>
    /// All other 4xx codes (404 Schema Not Found, 409 Incompatible, 422 Invalid,
    /// …) are deterministic poison and return <see langword="false"/>. A plain
    /// <see cref="HttpRequestException"/> (no HTTP response at all) is a pure
    /// network failure and is transient.
    /// </summary>
    public static bool IsTransientRegistryFailure(ConsumeException ex)
    {
        var current = ex.InnerException;
        while (current != null)
        {
            // Check SchemaRegistryException first (it IS-A HttpRequestException).
            if (current is SchemaRegistryException sre)
                return IsTransientStatus((int)sre.Status);
            if (current is HttpRequestException)
                return true; // pure network failure — no HTTP response at all
            current = current.InnerException;
        }
        return false;
    }

    private static bool IsTransientStatus(int status)
        => status >= 500   // server-side transient
           || status == 429 // Too Many Requests — throttling
           || status == 408; // Request Timeout
}
