// Bootstraps/refreshes docs/kafka-migration/schemas/latest/*.avsc by registering the
// current canonical schemas against a schema registry (Karapace/Confluent-compatible)
// and writing the registry's acknowledged shape per subject.
//
// Usage: dotnet run -- [--registry http://localhost:8081] [--out <dir>]
//   [--topic-requests-new <name>] [--topic-requests-status <name>] [--topic-results-status <name>]
//
// Topic-name args are MANDATORY when run against a non-default registry
// (e.g. SEFE Karapace) — without them the tool would register schemas under
// the historical dorc.* default subjects (SPEC-S-017 §2 #9, AC-8).
//
// Intended to be run as a standalone developer / dry-run action when a schema
// change is intentionally introduced and the PR-gate snapshot baseline needs
// to move. DO NOT run automatically in CI — snapshot refreshes should be an
// explicit human PR that the PR-gate itself evaluates against the live
// registry.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Schemas;

static string RepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "src", "Dorc.sln"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
}

var repoRoot = RepoRoot();
var registryUrl = Environment.GetEnvironmentVariable("KAFKA_SCHEMA_REGISTRY") ?? "http://localhost:8081";
var outDir = Path.Combine(repoRoot, "docs", "kafka-migration", "schemas", "latest");
var topics = new KafkaTopicsOptions();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--registry" when i + 1 < args.Length: registryUrl = args[++i]; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "--topic-requests-new" when i + 1 < args.Length: topics.RequestsNew = args[++i]; break;
        case "--topic-requests-status" when i + 1 < args.Length: topics.RequestsStatus = args[++i]; break;
        case "--topic-results-status" when i + 1 < args.Length: topics.ResultsStatus = args[++i]; break;
    }
}

Directory.CreateDirectory(outDir);
using var http = new HttpClient { BaseAddress = new Uri(registryUrl), Timeout = TimeSpan.FromSeconds(10) };

var pairs = new (string Subject, string Schema)[]
{
    ($"{topics.RequestsNew}-value", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{topics.RequestsStatus}-value", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{topics.ResultsStatus}-value", DorcEventSchemas.GenerateResultEventSchema())
};

Console.WriteLine($"[snapshot-schemas] registry={registryUrl}");
Console.WriteLine($"[snapshot-schemas] subjects: {string.Join(", ", pairs.Select(p => p.Subject))}");

foreach (var (subject, schema) in pairs)
{
    // Register if not already present. Confluent/Karapace dedupes by fingerprint
    // so repeated runs of the same schema do not create new versions.
    var body = new { schema, schemaType = "AVRO" };
    var register = await http.PostAsJsonAsync($"/subjects/{subject}/versions", body);
    register.EnsureSuccessStatusCode();

    var latestJson = await http.GetStringAsync($"/subjects/{subject}/versions/latest");
    var stored = JsonDocument.Parse(latestJson).RootElement.GetProperty("schema").GetString()!;
    var canonical = Canonicalise(JsonNode.Parse(stored))!.ToJsonString();

    var outPath = Path.Combine(outDir, $"{subject}.avsc");
    File.WriteAllText(outPath, canonical);
    Console.WriteLine($"Wrote {outPath} ({canonical.Length} bytes)");
}

static JsonNode? Canonicalise(JsonNode? node)
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
