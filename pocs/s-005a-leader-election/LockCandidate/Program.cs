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

// Path-traversal sanitisation for env-var-supplied file paths. Aikido's
// taint analysis is per-function and recognises string-mutation operations
// (.Replace, Path.GetFileName) as sanitisers — throw-based rejects do NOT
// break the taint chain in its model. So we strip `..` and path separators
// here at intake AND re-sanitise at every File.* sink via
// `Path.Join(cwd, Path.GetFileName(p))`. In practice the POC's test scripts
// pass simple filenames (e.g. `state-i1.jsonl`); both layers are
// defence-in-depth, not a behaviour change.
static string ReqLocalPath(string name)
{
    var raw = Req(name);
    var sanitised = raw
        .Replace("..", string.Empty, StringComparison.Ordinal)
        .Replace('/', '_')
        .Replace('\\', '_');
    return Path.Join(Path.GetFullPath(Environment.CurrentDirectory), sanitised);
}

var instanceId = Req("INSTANCE_ID");
var bootstrap = Req("KAFKA_BOOTSTRAP");
var topic = Req("TOPIC");
var groupId = Req("GROUP_ID");
var stateFile = ReqLocalPath("STATE_FILE");
var handlerInvocationsFile = ReqLocalPath("HANDLER_INVOCATIONS_FILE");
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
    var safeInvPath = Path.Join(
        Path.GetFullPath(Environment.CurrentDirectory),
        Path.GetFileName(handlerInvocationsFile));
    lock (stateLock)
    {
        File.AppendAllText(safeInvPath, JsonSerializer.Serialize(new
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
    // Re-sanitise inside the function — Aikido's taint analysis is
    // per-function, so the intake-time strip in ReqLocalPath isn't visible
    // to the analyser at this sink. Strip every byte the rule cares about
    // by reducing the path to its filename component combined with the
    // working directory; the analyser recognises this as a sanitiser.
    var safePath = Path.Join(
        Path.GetFullPath(Environment.CurrentDirectory),
        Path.GetFileName(stateFile));
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
        File.AppendAllText(safePath, record + Environment.NewLine);
        return true;
    }
}

static (int Version, string State)? ReadLatestVersion(string stateFile, string requestId)
{
    // Re-sanitise per-function (see ApplyIdempotently note).
    var safePath = Path.Join(
        Path.GetFullPath(Environment.CurrentDirectory),
        Path.GetFileName(stateFile));
    if (!File.Exists(safePath)) return null;
    return File.ReadLines(safePath)
        .Where(l => !string.IsNullOrWhiteSpace(l))
        .Select(ParseRecord)
        .Where(r => r.RequestId == requestId)
        .Aggregate<(string RequestId, int Version, string State), (int Version, string State)?>(
            null,
            (max, r) => max is null || r.Version > max.Value.Version ? (r.Version, r.State) : max);

    // Local helper so JsonDocument disposes per-line — keeping the LINQ
    // chain free of disposable leaks.
    static (string RequestId, int Version, string State) ParseRecord(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        return (
            root.GetProperty("requestId").GetString() ?? string.Empty,
            root.GetProperty("version").GetInt32(),
            root.GetProperty("state").GetString() ?? string.Empty);
    }
}
