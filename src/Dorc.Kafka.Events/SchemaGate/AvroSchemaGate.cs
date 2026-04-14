using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dorc.Core.Events;
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
/// </summary>
public sealed class AvroSchemaGate
{
    private readonly HttpClient? _registryHttp;
    private readonly string _canonicalDir;
    private readonly string? _snapshotDir;

    public AvroSchemaGate(HttpClient? registryHttp, string canonicalDir, string? snapshotDir)
    {
        _registryHttp = registryHttp;
        _canonicalDir = canonicalDir;
        _snapshotDir = snapshotDir;
    }

    public static IReadOnlyList<(string Subject, string Schema)> InScopeSchemas() => new[]
    {
        (KafkaSubjectNames.RequestsNewValue, DorcEventSchemas.GenerateRequestEventSchema()),
        (KafkaSubjectNames.RequestsStatusValue, DorcEventSchemas.GenerateRequestEventSchema()),
        (KafkaSubjectNames.ResultsStatusValue, DorcEventSchemas.GenerateResultEventSchema())
    };

    public async Task<IReadOnlyList<GateReport>> RunAsync(CancellationToken cancellationToken = default)
    {
        var reports = new List<GateReport>();
        foreach (var (subject, regenerated) in InScopeSchemas())
            reports.Add(await CheckSubjectAsync(subject, regenerated, cancellationToken));
        return reports;
    }

    internal async Task<GateReport> CheckSubjectAsync(string subject, string regenerated, CancellationToken cancellationToken)
    {
        // Step 2: regenerated must match checked-in canonical.
        var canonicalPath = Path.Combine(_canonicalDir, $"{subject}.avsc");
        if (!File.Exists(canonicalPath))
            return new GateReport(GateOutcome.Fail, subject, $"Canonical schema file not found at {canonicalPath}.");

        var canonical = await File.ReadAllTextAsync(canonicalPath, cancellationToken);
        if (!SchemasEquivalent(canonical, regenerated))
            return new GateReport(GateOutcome.Fail, subject,
                $"Regenerated schema does not match canonical file {canonicalPath}. Run tools/generate-schemas to refresh.");

        // Step 3–4: BACKWARD compatibility vs the latest known schema.
        var liveOutcome = await TryCompatibilityAgainstLiveAsync(subject, regenerated, cancellationToken);
        if (liveOutcome is not null) return liveOutcome;

        var snapshotOutcome = TryCompatibilityAgainstSnapshot(subject, regenerated);
        if (snapshotOutcome is not null) return snapshotOutcome;

        return new GateReport(GateOutcome.Fail, subject,
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

            if (!response.IsSuccessStatusCode)
                return null; // live unreachable / transient; fall through to snapshot

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
    }

    private GateReport? TryCompatibilityAgainstSnapshot(string subject, string regenerated)
    {
        if (string.IsNullOrEmpty(_snapshotDir)) return null;

        var snapshotPath = Path.Combine(_snapshotDir, $"{subject}.avsc");
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
            return new GateReport(GateOutcome.Pass, subject, "Schema unchanged against committed snapshot.", Source: "snapshot");

        return new GateReport(GateOutcome.Fail, subject,
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
