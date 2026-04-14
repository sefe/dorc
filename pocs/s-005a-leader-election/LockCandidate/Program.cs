// S-005a POC — Option (i): Kafka consumer-group + per-Request idempotency.
//
// A "lock candidate" (think Monitor instance). N of these form a consumer
// group on `dorc.poc.requests`. Partition ownership = lock ownership for
// any RequestId that hashes to that partition. The idempotent handler is
// a synthetic "apply state transition" that writes to a shared JSON-lines
// file; duplicate invocations for the same (RequestId, Version) are
// coalesced by checking current state.
//
// Environment:
//   INSTANCE_ID        — logical name of this candidate (goes into logs)
//   KAFKA_BOOTSTRAP    — e.g. localhost:9092
//   TOPIC              — e.g. dorc.poc.requests
//   GROUP_ID           — shared across all candidates in this test run
//   STATE_FILE         — JSON-lines file recording accepted state transitions
//   HANDLER_INVOCATIONS_FILE — every handler call (including idempotent
//                              no-ops) appended here for post-run analysis
//   FORCE_PRE_COMMIT_DELAY_MS (optional) — sleeps this long between
//                              handler complete and offset commit; useful
//                              for forcing duplicate invocation on
//                              partition reassignment
//
// Terminates on SIGINT/SIGTERM; logs each assigned/revoked event + each
// handler call (success or idempotent no-op).

using System.Text.Json;
using Confluent.Kafka;

static string Req(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Env var {name} is required.");
static string Opt(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;

var instanceId = Req("INSTANCE_ID");
var bootstrap = Req("KAFKA_BOOTSTRAP");
var topic = Req("TOPIC");
var groupId = Req("GROUP_ID");
var stateFile = Req("STATE_FILE");
var handlerInvocationsFile = Req("HANDLER_INVOCATIONS_FILE");
var forcePreCommitDelayMs = int.Parse(Opt("FORCE_PRE_COMMIT_DELAY_MS", "0"));

void Log(string kind, object payload)
{
    var line = JsonSerializer.Serialize(new
    {
        timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
        instanceId,
        kind,
        payload
    });
    Console.WriteLine(line);
}

var config = new ConsumerConfig
{
    BootstrapServers = bootstrap,
    GroupId = groupId,
    ClientId = instanceId,
    EnableAutoCommit = false,
    AutoOffsetReset = AutoOffsetReset.Earliest,
    SessionTimeoutMs = 10_000,
    HeartbeatIntervalMs = 3_000,
    MaxPollIntervalMs = 60_000,
    PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
};

using var consumer = new ConsumerBuilder<string, string>(config)
    .SetPartitionsAssignedHandler((_, partitions) =>
        Log("partitions-assigned", new { partitions = partitions.Select(p => p.Partition.Value).ToArray() }))
    .SetPartitionsRevokedHandler((_, partitions) =>
        Log("partitions-revoked", new { partitions = partitions.Select(p => p.Partition.Value).ToArray() }))
    .SetPartitionsLostHandler((_, partitions) =>
        Log("partitions-lost", new { partitions = partitions.Select(p => p.Partition.Value).ToArray() }))
    .SetErrorHandler((_, error) => Log("consumer-error", new { code = error.Code.ToString(), reason = error.Reason }))
    .Build();

consumer.Subscribe(topic);
Log("subscribed", new { topic, groupId });

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

var stateLock = new object();   // coarse file lock within this process

while (!cts.IsCancellationRequested)
{
    ConsumeResult<string, string>? result;
    try { result = consumer.Consume(cts.Token); }
    catch (OperationCanceledException) { break; }

    if (result is null) continue;

    // Handler input: key = RequestId, value = "version:<int>|state:<str>"
    var key = result.Message.Key;
    var value = result.Message.Value;
    var (version, nextState) = ParseValue(value);

    var accepted = ApplyIdempotently(stateFile, key, version, nextState, instanceId, stateLock);

    Log("handler-invoked", new
    {
        topic = result.Topic,
        partition = result.Partition.Value,
        offset = result.Offset.Value,
        requestId = key,
        version,
        nextState,
        outcome = accepted ? "accepted" : "idempotent-noop"
    });

    // Also record every invocation (even no-ops) into the append-only
    // handler-invocations file so the post-run analysis can distinguish
    // "handler ran twice but second was no-op" from "handler ran once".
    lock (stateLock)
    {
        File.AppendAllText(handlerInvocationsFile, JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            instanceId,
            partition = result.Partition.Value,
            offset = result.Offset.Value,
            requestId = key,
            version,
            outcome = accepted ? "accepted" : "idempotent-noop"
        }) + Environment.NewLine);
    }

    if (forcePreCommitDelayMs > 0)
    {
        try { await Task.Delay(forcePreCommitDelayMs, cts.Token); }
        catch (OperationCanceledException) { break; }
    }

    try { consumer.Commit(result); }
    catch (KafkaException ex) { Log("commit-failed", new { code = ex.Error.Code.ToString(), reason = ex.Error.Reason }); }
}

try { consumer.Close(); } catch { /* best effort */ }
Log("shutdown", new { reason = "cancelled" });

static (int Version, string State) ParseValue(string v)
{
    // Format: "version:<int>|state:<str>"
    var parts = v.Split('|');
    var version = int.Parse(parts[0]["version:".Length..]);
    var state = parts[1]["state:".Length..];
    return (version, state);
}

static bool ApplyIdempotently(string stateFile, string requestId, int version, string nextState, string byInstance, object stateLock)
{
    lock (stateLock)
    {
        var latest = ReadLatestVersion(stateFile, requestId);
        if (latest is not null && latest.Value.Version >= version)
            return false;   // idempotent no-op (stale or same)

        var record = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            requestId,
            version,
            state = nextState,
            appliedBy = byInstance
        });
        File.AppendAllText(stateFile, record + Environment.NewLine);
        return true;
    }
}

static (int Version, string State)? ReadLatestVersion(string stateFile, string requestId)
{
    if (!File.Exists(stateFile)) return null;
    (int Version, string State)? latest = null;
    foreach (var line in File.ReadLines(stateFile))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.GetProperty("requestId").GetString() != requestId) continue;
        var ver = root.GetProperty("version").GetInt32();
        var state = root.GetProperty("state").GetString() ?? string.Empty;
        if (latest is null || ver > latest.Value.Version)
            latest = (ver, state);
    }
    return latest;
}
