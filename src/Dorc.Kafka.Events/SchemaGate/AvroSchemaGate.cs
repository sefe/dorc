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
/// PR-gate logic. Requires backward-enforcing compatibility: the registry's
/// POST /compatibility endpoint evaluates against the SUBJECT'S configured
/// mode (a registry running NONE would pass anything), so the gate first
/// verifies the effective mode (subject-level GET /config/{subject}, falling
/// back to global GET /config) is one of BACKWARD, BACKWARD_TRANSITIVE,
/// FULL, or FULL_TRANSITIVE and fails closed otherwise. Source selection:
/// prefer live Karapace when reachable; fall back to a committed snapshot;
/// fail closed if neither is available.
///
/// <see cref="InScopeSchemas"/> emits
/// <c>(CanonicalKey, LiveSubject, Schema)</c> triples. <c>CanonicalKey</c>
/// is derived from <see cref="KafkaTopicsOptions"/> defaults and drives
/// both <c>current/</c> (canonical-equality) and <c>latest/</c> (snapshot-
/// fallback) on-disk file lookups; <c>LiveSubject</c> is derived from the
/// *deployed* options and drives the live-registry POST URL. When defaults
/// and deployed names match (the production default), the two are equal
/// the indirection only matters when the deployed cluster (e.g. SEFE Aiven)
/// uses an enterprise topic-naming convention.
/// </summary>
public sealed class AvroSchemaGate
{
    // Canonical schema-snapshot filenames are environment-neutral by design
    // — they always reflect the historical default topic
    // names so committed.avsc files diff cleanly across environments.
    // The.avsc filenames returned by ResolveCanonicalAvscFileName are
    // hard-coded string literals (not interpolated from canonicalKey) so
    // Aikido's PathTraversal taint analysis sees no flow from the parameter
    // into the file-read sink — the only inputs to Path.Combine are the
    // injected directory and a literal from a fixed set.
    internal const string CanonicalRequestsNewKey    = "dorc.requests.new-value";
    internal const string CanonicalRequestsStatusKey = "dorc.requests.status-value";
    internal const string CanonicalResultsStatusKey  = "dorc.results.status-value";
    internal const string CanonicalRequestsNewDlqKey = "dorc.requests.new.dlq-value";

    private const string CanonicalRequestsNewAvsc    = "dorc.requests.new-value.avsc";
    private const string CanonicalRequestsStatusAvsc = "dorc.requests.status-value.avsc";
    private const string CanonicalResultsStatusAvsc  = "dorc.results.status-value.avsc";
    private const string CanonicalRequestsNewDlqAvsc = "dorc.requests.new.dlq-value.avsc";

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
        (CanonicalResultsStatusKey,  $"{_deployedTopics.ResultsStatus}-value", DorcEventSchemas.GenerateResultEventSchema()),
        (CanonicalRequestsNewDlqKey, $"{_deployedTopics.RequestsNewDlq}-value", DorcEventSchemas.GenerateErrorEnvelopeSchema())
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
        // contracts stay environment-neutral. Filename
        // resolved through a literal-arm switch so no parameter content flows
        // into Path.Combine.
        var canonicalFile = canonicalKey switch
        {
            CanonicalRequestsNewKey    => CanonicalRequestsNewAvsc,
            CanonicalRequestsStatusKey => CanonicalRequestsStatusAvsc,
            CanonicalResultsStatusKey  => CanonicalResultsStatusAvsc,
            CanonicalRequestsNewDlqKey => CanonicalRequestsNewDlqAvsc,
            _ => throw new ArgumentException($"Unknown canonical schema key: {canonicalKey}", nameof(canonicalKey))
        };
        // Path.Join (not Path.Combine) — Path.Combine silently drops the base
        // directory if the leaf is rooted; Path.Join always concatenates.
        // canonicalFile is a const-switch literal anyway, but the analyzer
        // can't see that and Path.Join keeps it (and any future caller) safe.
        var canonicalPath = Path.Join(_canonicalDir, canonicalFile);
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

    /// <summary>
    /// Compatibility modes under which a POST /compatibility "is_compatible"
    /// answer actually proves backward evolution safety. Anything else
    /// (NONE, NONE_TRANSITIVE, FORWARD, FORWARD_TRANSITIVE) would let an
    /// incompatible schema sail through the POST check.
    /// </summary>
    private static readonly string[] BackwardEnforcingModes =
    {
        "BACKWARD", "BACKWARD_TRANSITIVE", "FULL", "FULL_TRANSITIVE"
    };

    private async Task<GateReport?> TryCompatibilityAgainstLiveAsync(string subject, string regenerated, CancellationToken cancellationToken)
    {
        if (_registryHttp is null) return null;

        try
        {
            // POST /compatibility evaluates against the subject's CONFIGURED
            // mode — a registry running NONE passes anything. Verify the
            // effective mode first and fail closed if it doesn't enforce
            // backward evolution safety.
            var (effectiveMode, modeDetermined) = await ResolveEffectiveCompatibilityAsync(subject, cancellationToken);
            if (!modeDetermined)
                return null; // registry config unreadable (transient) — fall through to snapshot
            if (!BackwardEnforcingModes.Contains(effectiveMode, StringComparer.OrdinalIgnoreCase))
                return new GateReport(GateOutcome.Fail, subject,
                    $"Live registry effective compatibility mode is '{effectiveMode}', which does not enforce backward evolution safety. " +
                    $"Fix the registry config (e.g. PUT /config/{subject} {{\"compatibility\":\"BACKWARD\"}} or set the global default) before relying on this gate.",
                    Source: "live");

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
            {
                // Only a Confluent/Karapace 40401 ("subject not found") body
                // proves the subject genuinely isn't registered yet. A bare
                // 404 (mis-pathed proxy, wrong base URL) must NOT pass — it
                // would silently green-light every PR forever.
                var notFoundBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (HasRegistryErrorCode(notFoundBody, 40401))
                    return new GateReport(GateOutcome.Pass, subject,
                        "Subject not yet registered in live registry (40401); compatibility check skipped (first version is always compatible).",
                        Source: "live");
                return new GateReport(GateOutcome.Fail, subject,
                    $"Live registry returned 404 WITHOUT a schema-registry 40401 error body for subject '{subject}' — likely a mis-pathed proxy or wrong registry base URL. Response body: {Truncate(notFoundBody, 500)}",
                    Source: "live");
            }

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

    /// <summary>
    /// Resolves the registry's effective compatibility mode for a subject:
    /// subject-level <c>GET /config/{subject}</c> first; on 404 (no
    /// subject-level override) fall back to global <c>GET /config</c>.
    /// Returns <c>(mode, true)</c> when determined, <c>(null, false)</c> when
    /// the config endpoints are unreadable (caller falls through to the
    /// snapshot path, which fails closed on any schema change).
    /// </summary>
    private async Task<(string? Mode, bool Determined)> ResolveEffectiveCompatibilityAsync(
        string subject, CancellationToken cancellationToken)
    {
        var subjectResponse = await _registryHttp!.GetAsync($"/config/{subject}", cancellationToken);
        if (subjectResponse.IsSuccessStatusCode)
        {
            var mode = ReadCompatibilityLevel(await subjectResponse.Content.ReadAsStringAsync(cancellationToken));
            return mode is null ? (null, false) : (mode, true);
        }

        if (subjectResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
            return (null, false); // 5xx / auth / proxy issue — can't determine

        var globalResponse = await _registryHttp.GetAsync("/config", cancellationToken);
        if (!globalResponse.IsSuccessStatusCode)
            return (null, false);

        var globalMode = ReadCompatibilityLevel(await globalResponse.Content.ReadAsStringAsync(cancellationToken));
        return globalMode is null ? (null, false) : (globalMode, true);
    }

    /// <summary>
    /// Parses <c>compatibilityLevel</c> (Confluent/Karapace GET /config shape;
    /// also accepts <c>compatibility</c> for registries that echo the PUT
    /// shape). Returns null when the body isn't a recognisable config
    /// document — e.g. a proxy's HTML error page on a 200.
    /// </summary>
    private static string? ReadCompatibilityLevel(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty("compatibilityLevel", out var level) && level.ValueKind == JsonValueKind.String)
                return level.GetString();
            if (doc.RootElement.TryGetProperty("compatibility", out var compat) && compat.ValueKind == JsonValueKind.String)
                return compat.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the response body is a schema-registry error document with
    /// the given <c>error_code</c> (Confluent and Karapace share the shape).
    /// </summary>
    internal static bool HasRegistryErrorCode(string body, int errorCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error_code", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.TryGetInt32(out var value)
                && value == errorCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private GateReport? TryCompatibilityAgainstSnapshot(string canonicalKey, string liveSubject, string regenerated)
    {
        if (string.IsNullOrEmpty(_snapshotDir)) return null;

        var snapshotFile = canonicalKey switch
        {
            CanonicalRequestsNewKey    => CanonicalRequestsNewAvsc,
            CanonicalRequestsStatusKey => CanonicalRequestsStatusAvsc,
            CanonicalResultsStatusKey  => CanonicalResultsStatusAvsc,
            CanonicalRequestsNewDlqKey => CanonicalRequestsNewDlqAvsc,
            _ => throw new ArgumentException($"Unknown canonical schema key: {canonicalKey}", nameof(canonicalKey))
        };
        // Same Path.Join (not Combine) safety as the canonical-file path above.
        var snapshotPath = Path.Join(_snapshotDir, snapshotFile);
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
        var l = AvroJsonCanonicaliser.Canonicalise(JsonNode.Parse(left))?.ToJsonString();
        var r = AvroJsonCanonicaliser.Canonicalise(JsonNode.Parse(right))?.ToJsonString();
        return string.Equals(l, r, StringComparison.Ordinal);
    }
}
