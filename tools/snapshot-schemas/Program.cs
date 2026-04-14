// Bootstraps/refreshes docs/kafka-migration/schemas/latest/*.avsc by registering the
// current canonical schemas against a schema registry (Karapace/Confluent-compatible)
// and writing the registry's acknowledged shape per subject.
//
// Usage: dotnet run -- [--registry http://localhost:8081] [--out docs/kafka-migration/schemas/latest]
//
// Intended to be run as a standalone developer action when a schema change is
// intentionally introduced and the PR-gate snapshot baseline needs to move.
// DO NOT run automatically in CI — snapshot refreshes should be an explicit
// human PR that the PR-gate itself evaluates against the live registry.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dorc.Kafka.Events;
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

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--registry": registryUrl = args[++i]; break;
        case "--out": outDir = args[++i]; break;
    }
}

Directory.CreateDirectory(outDir);
using var http = new HttpClient { BaseAddress = new Uri(registryUrl), Timeout = TimeSpan.FromSeconds(10) };

var pairs = new (string Subject, string Schema)[]
{
    (KafkaSubjectNames.RequestsNewValue, DorcEventSchemas.GenerateRequestEventSchema()),
    (KafkaSubjectNames.RequestsStatusValue, DorcEventSchemas.GenerateRequestEventSchema()),
    (KafkaSubjectNames.ResultsStatusValue, DorcEventSchemas.GenerateResultEventSchema())
};

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
