using System.Net;
using System.Net.Http;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Dorc.Kafka.Events.Publisher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Pins the C4 fix: <c>IsRegistryConnectivityFailure</c> must distinguish
/// transient schema-registry failures from deterministic poison messages.
/// <para>
/// The key subtlety: <see cref="SchemaRegistryException"/> inherits from
/// <see cref="HttpRequestException"/>, so the subtype check order matters —
/// matching <see cref="HttpRequestException"/> first would swallow 4xx
/// errors (e.g. 40403 Schema Not Found) as transient, causing an infinite
/// backoff loop that permanently stalls the partition instead of routing
/// to the DLQ.
/// </para>
/// </summary>
[TestClass]
public class RegistryConnectivityFailureTests
{
    private static readonly ConsumeResult<byte[], byte[]> AnyRecord = new();
    private static readonly Error AnyError = new(ErrorCode.Local_Application, "avro-deserialize");

    private static ConsumeException Wrap(Exception inner)
        => new(AnyRecord, AnyError, inner);

    // ── Requests consumer ──────────────────────────────────────────────────

    [TestMethod]
    public void Requests_HttpRequestException_IsTransient()
    {
        var ex = Wrap(new HttpRequestException("connection refused"));
        Assert.IsTrue(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "Pure network failure (no HTTP response) must be treated as transient — back off, do not DLQ.");
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_500_IsTransient()
    {
        var ex = Wrap(new SchemaRegistryException("Internal Server Error", HttpStatusCode.InternalServerError, 50001));
        Assert.IsTrue(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 500 from schema registry is a transient server error — must back off, not DLQ.");
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_503_IsTransient()
    {
        var ex = Wrap(new SchemaRegistryException("Service Unavailable", HttpStatusCode.ServiceUnavailable, 50301));
        Assert.IsTrue(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 503 from schema registry is a transient server error — must back off, not DLQ.");
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_404_IsPoisonMessage()
    {
        // 40403 = Schema Not Found — the message will NEVER deserialise.
        // Must go to DLQ rather than looping in backoff indefinitely.
        var ex = Wrap(new SchemaRegistryException("Schema not found", HttpStatusCode.NotFound, 40403));
        Assert.IsFalse(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 404 (schema not found) is a poison-message condition — must route to DLQ, not back off.");
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_409_IsPoisonMessage()
    {
        var ex = Wrap(new SchemaRegistryException("Incompatible schema", HttpStatusCode.Conflict, 40901));
        Assert.IsFalse(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 409 (incompatible schema) is a deterministic client error — must route to DLQ.");
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_422_IsPoisonMessage()
    {
        var ex = Wrap(new SchemaRegistryException("Invalid schema", HttpStatusCode.UnprocessableEntity, 42201));
        Assert.IsFalse(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex));
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_429_IsTransient()
    {
        // Audit CR#2: 429 Too Many Requests is registry throttling under load,
        // not a poison message. The event is valid and will deserialise once the
        // throttle clears — must back off, not DLQ.
        var ex = Wrap(new SchemaRegistryException("Too Many Requests", (HttpStatusCode)429, 42901));
        Assert.IsTrue(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 429 (throttling) must be treated as transient — back off, do not DLQ.");
    }

    [TestMethod]
    public void Requests_SchemaRegistryException_408_IsTransient()
    {
        var ex = Wrap(new SchemaRegistryException("Request Timeout", (HttpStatusCode)408, 40801));
        Assert.IsTrue(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 408 (request timeout) is retryable — back off, do not DLQ.");
    }

    [TestMethod]
    public void Requests_NoInnerException_IsNotTransient()
    {
        var ex = new ConsumeException(AnyRecord, AnyError);
        Assert.IsFalse(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "ConsumeException with no inner exception must not trigger backoff.");
    }

    [TestMethod]
    public void Requests_NestedHttpRequestException_IsTransient()
    {
        // Verify the while(current.InnerException) walk reaches nested exceptions.
        var network = new HttpRequestException("network error");
        var wrapper = new InvalidOperationException("avro wrapper", network);
        var ex = Wrap(wrapper);
        Assert.IsTrue(DeploymentRequestsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HttpRequestException nested inside another exception must still be detected.");
    }

    // ── Results consumer — same logic, separate implementation ────────────

    [TestMethod]
    public void Results_HttpRequestException_IsTransient()
    {
        var ex = Wrap(new HttpRequestException("connection refused"));
        Assert.IsTrue(DeploymentResultsKafkaConsumer.IsRegistryConnectivityFailure(ex));
    }

    [TestMethod]
    public void Results_SchemaRegistryException_500_IsTransient()
    {
        var ex = Wrap(new SchemaRegistryException("Internal Server Error", HttpStatusCode.InternalServerError, 50001));
        Assert.IsTrue(DeploymentResultsKafkaConsumer.IsRegistryConnectivityFailure(ex));
    }

    [TestMethod]
    public void Results_SchemaRegistryException_404_IsPoisonMessage()
    {
        var ex = Wrap(new SchemaRegistryException("Schema not found", HttpStatusCode.NotFound, 40403));
        Assert.IsFalse(DeploymentResultsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 404 must route to HandleFailure on the results consumer, not infinite backoff.");
    }

    [TestMethod]
    public void Results_SchemaRegistryException_429_IsTransient()
    {
        // Audit CR#2: results.status has no DLQ, so misclassifying a throttled
        // (429) valid event as poison would permanently drop a real-time UI event.
        var ex = Wrap(new SchemaRegistryException("Too Many Requests", (HttpStatusCode)429, 42901));
        Assert.IsTrue(DeploymentResultsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "HTTP 429 (throttling) must back off on the results consumer, not drop the event.");
    }

    [TestMethod]
    public void Results_SchemaRegistryException_408_IsTransient()
    {
        var ex = Wrap(new SchemaRegistryException("Request Timeout", (HttpStatusCode)408, 40801));
        Assert.IsTrue(DeploymentResultsKafkaConsumer.IsRegistryConnectivityFailure(ex));
    }

    [TestMethod]
    public void Results_SchemaRegistryException_IsAHttpRequestException_SubtypeCheckOrdering()
    {
        // Documents the IS-A relationship and validates the subtype-first check.
        // SchemaRegistryException IS-A HttpRequestException. If we checked
        // HttpRequestException first, ALL SchemaRegistryExceptions (including
        // 4xx poison messages) would be falsely classified as transient.
        var sre = new SchemaRegistryException("Not Found", HttpStatusCode.NotFound, 40403);
        Assert.IsTrue(sre is HttpRequestException,
            "SchemaRegistryException must inherit HttpRequestException — if this fails the " +
            "Confluent library changed its hierarchy and the production code must be reviewed.");

        var ex = Wrap(sre);
        Assert.IsFalse(DeploymentResultsKafkaConsumer.IsRegistryConnectivityFailure(ex),
            "Even though SchemaRegistryException IS-A HttpRequestException, the subtype-first check " +
            "must classify 4xx SchemaRegistryException as a poison message, not a transient failure.");
    }
}
