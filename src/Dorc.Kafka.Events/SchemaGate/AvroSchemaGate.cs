using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dorc.Core.Events;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Schemas;

namespace Dorc.Kafka.Events.SchemaGate;

public enum GateOutcome
{
    Pass,
    Fail
}

public sealed record GateReport(GateOutcome Outcome, string Subject, string Message, string? Source = null);

/// <summary>
/// PR-gate logic per SPEC-S-003 R-5. Always enforces BACKWARD compatibility
/// regardless of any observed registry mode. Source selection: prefer live
/// Karapace when reachable; fall back to a committed snapshot; fail closed
/// if neither is available.
///
/// Per SPEC-S-017 §2 #4 + R1.1: <see cref="InScopeSchemas"/> emits
/// <c>(CanonicalKey, LiveSubject, Schema)</c> triples. <c>CanonicalKey</c>
/// is derived from <see cref="KafkaTopicsOptions"/> defaults and drives
/// both <c>current/</c> (canonical-equality) and <c>latest/</c> (snapshot-
/// fallback) on-disk file lookups; <c>LiveSubject</c> is derived from the
/// *deployed* options and drives the live-registry POST URL. When defaults
/// and deployed names match (the production default), the two are equal —
/// the indirection only matters when the deployed cluster (e.g. SEFE Aiven)
/// uses an enterprise topic-naming convention.
/// </summary>
public sealed class AvroSchemaGate
{
    // Canonical schema-snapshot filenames are environment-neutral by design
    // (SPEC-S-017 §2 #4) — they always reflect the historical default topic
    // names so committed .avsc files diff cleanly across environments.
    // Held as `const string` rather than derived from KafkaTopicsOptions so
    // the file-read sinks below trace back to a compile-time constant, not
    // to a config-bound type. (Aikido path-traversal finding 2026-04-27.)
    internal const string CanonicalRequestsNewKey    = "dorc.requests.new-value";
    internal const string CanonicalRequestsStatusKey = "dorc.requests.status-value";
    internal const string CanonicalResultsStatusKey  = "dorc.results.status-value";

    private readonly KafkaTopicsOptions _deployedTopics;
    private readonly HttpClient? _registryHttp;
    private readonly string _canonicalDir;
    private readonly string? _snapshotDir;

    public AvroSchemaGate(
        KafkaTopicsOptions deployedTopics,
        HttpClient? registryHttp,
        string canonicalDir,
        string? snapshotDir)
    {
        _deployedTopics = deployedTopics;
        _registryHttp = registryHttp;
        _canonicalDir = canonicalDir;
        _snapshotDir = snapshotDir;
    }

    /// <summary>
    /// Convenience constructor — uses <see cref="KafkaTopicsOptions"/> defaults
    /// for the deployed live-subject names. Suitable for the dev-time
    /// <c>tools/schema-gate</c> program against a local registry, and for
    /// tests that don't exercise the diverged-name path.
    /// </summary>
    public AvroSchemaGate(HttpClient? registryHttp, string canonicalDir, string? snapshotDir)
        : this(new KafkaTopicsOptions(), registryHttp, canonicalDir, snapshotDir) { }

    public IReadOnlyList<(string CanonicalKey, string LiveSubject, string Schema)> InScopeSchemas() => new[]
    {
        (CanonicalRequestsNewKey,    $"{_deployedTopics.RequestsNew}-value",    DorcEventSchemas.GenerateRequestEventSchema()),
        (CanonicalRequestsStatusKey, $"{_deployedTopics.RequestsStatus}-value", DorcEventSchemas.GenerateRequestEventSchema()),
        (CanonicalResultsStatusKey,  $"{_deployedTopics.ResultsStatus}-value", DorcEventSchemas.GenerateResultEventSchema())
    };

    public async Task<IReadOnlyList<GateReport>> RunAsync(CancellationToken cancellationToken = default)
    {
        var reports = new List<GateReport>();
        foreach (var (canonicalKey, liveSubject, regenerated) in InScopeSchemas())
            reports.Add(await CheckSubjectAsync(canonicalKey, liveSubject, regenerated, cancellationToken));
        return reports;
    }

    internal async Task<GateReport> CheckSubjectAsync(
        string canonicalKey,
        string liveSubject,
        string regenerated,
        CancellationToken cancellationToken)
    {
        // Step 2: regenerated must match checked-in canonical. File lookup is
        // keyed off the *default*-derived canonicalKey so committed schema
        // contracts stay environment-neutral (SPEC-S-017 §2 #4).
        var canonicalPath = Path.Combine(_canonicalDir, $"{canonicalKey}.avsc");
        if (!File.Exists(canonicalPath))
            return new GateReport(GateOutcome.Fail, liveSubject, $"Canonical schema file not found at {canonicalPath}.");

        var canonical = await File.ReadAllTextAsync(canonicalPath, cancellationToken);
        // Strict byte equality: the canonical is produced by tools/generate-schemas
        // and must be exactly what the generator emits. Canonicalisation lives on
        // the *registry*-returned schema (where Karapace reorders keys on storage).
        if (!string.Equals(canonical, regenerated, StringComparison.Ordinal))
            return new GateReport(GateOutcome.Fail, liveSubject,
                $"Regenerated schema does not match canonical file {canonicalPath}. Run tools/generate-schemas to refresh.");

        // Step 3–4: BACKWARD compatibility vs the latest known schema. Live
        // POST uses the *deployed* liveSubject; snapshot lookup uses the
        // *default*-derived canonicalKey.
        var liveOutcome = await TryCompatibilityAgainstLiveAsync(liveSubject, regenerated, cancellationToken);
        if (liveOutcome is not null) return liveOutcome;

        var snapshotOutcome = TryCompatibilityAgainstSnapshot(canonicalKey, liveSubject, regenerated);
        if (snapshotOutcome is not null) return snapshotOutcome;

        return new GateReport(GateOutcome.Fail, liveSubject,
            "Neither live schema registry nor committed snapshot available; cannot determine latest registered schema. Gate fails closed.");
    }

    private async Task<GateReport?> TryCompatibilityAgainstLiveAsync(string subject, string regenerated, CancellationToken cancellationToken)
    {
        if (_registryHttp is null) return null;

        try
        {
            var body = new
            {
                schema = regenerated,
                schemaType = "AVRO"
            };
            var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(
                _registryHttp,
                $"/compatibility/subjects/{subject}/versions/latest?verbose=true",
                body,
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new GateReport(GateOutcome.Pass, subject,
                    "Subject not yet registered in live registry; compatibility check skipped (first version is always compatible).",
                    Source: "live");

            if ((int)response.StatusCode >= 500)
                return null; // 5xx: transient, fall through to snapshot

            if (!response.IsSuccessStatusCode)
            {
                // 4xx other than 404: the registry rejected the request itself
                // (malformed body, validation error). Fail closed rather than
                // falling through to a snapshot that could mask a real problem.
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new GateReport(GateOutcome.Fail, subject,
                    $"Live registry rejected compatibility request ({(int)response.StatusCode}): {errorBody}",
                    Source: "live");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);
            var isCompatible = doc.RootElement.GetProperty("is_compatible").GetBoolean();
            if (isCompatible)
                return new GateReport(GateOutcome.Pass, subject, "BACKWARD-compatible against live registry latest.", Source: "live");

            var messages = doc.RootElement.TryGetProperty("messages", out var m)
                ? string.Join("; ", m.EnumerateArray().Select(x => x.GetString()))
                : "(no details provided by registry)";
            return new GateReport(GateOutcome.Fail, subject,
                $"BACKWARD incompatibility against live registry latest: {messages}",
                Source: "live");
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private GateReport? TryCompatibilityAgainstSnapshot(string canonicalKey, string liveSubject, string regenerated)
    {
        if (string.IsNullOrEmpty(_snapshotDir)) return null;

        var snapshotPath = Path.Combine(_snapshotDir, $"{canonicalKey}.avsc");
        if (!File.Exists(snapshotPath)) return null;

        var snapshot = File.ReadAllText(snapshotPath);

        // Offline compatibility check: we can't fully evaluate Avro BACKWARD
        // semantics without a compat engine; the spec's fallback-to-snapshot
        // rule is "prove at least that the schema hasn't silently diverged
        // from the last registry-acknowledged state." If identical, pass; if
        // different, fail closed and demand the developer run against live
        // registry (or refresh the snapshot via a separate PR that itself
        // went through this gate against live).
        if (SchemasEquivalent(snapshot, regenerated))
            return new GateReport(GateOutcome.Pass, liveSubject, "Schema unchanged against committed snapshot.", Source: "snapshot");

        return new GateReport(GateOutcome.Fail, liveSubject,
            $"Schema differs from committed snapshot {snapshotPath} and live registry is unreachable. Gate fails closed; run against live registry or refresh snapshot in a dedicated PR.",
            Source: "snapshot");
    }

    internal static bool SchemasEquivalent(string left, string right)
    {
        var l = Canonicalise(JsonNode.Parse(left))?.ToJsonString();
        var r = Canonicalise(JsonNode.Parse(right))?.ToJsonString();
        return string.Equals(l, r, StringComparison.Ordinal);
    }

    private static JsonNode? Canonicalise(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var ordered = new JsonObject();
                foreach (var kv in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    ordered[kv.Key] = Canonicalise(kv.Value?.DeepClone());
                return ordered;
            case JsonArray arr:
                var newArr = new JsonArray();
                foreach (var item in arr)
                    newArr.Add(Canonicalise(item?.DeepClone()));
                return newArr;
            default:
                return node;
        }
    }
}
