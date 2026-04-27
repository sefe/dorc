using System.Text.Json.Nodes;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Schemas;

// Usage: dotnet run -- [target-dir]
//   [--topic-requests-new <name>] [--topic-requests-status <name>] [--topic-results-status <name>]
//
// Defaults: targetDir resolves to docs/kafka-migration/schemas/current/; topic
// names default to KafkaTopicsOptions defaults (dorc.requests.new etc.). Per
// SPEC-S-017 R2 the topic-name args are an explicit override surface so the
// tool can be invoked against a non-default registry (e.g. SEFE Karapace)
// during S-010 dry-run pre-flight.

static string RepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "src", "Dorc.sln"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("Could not locate repo root (src/Dorc.sln).");
}

string? targetDir = null;
var topics = new KafkaTopicsOptions();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--topic-requests-new" when i + 1 < args.Length:
            topics.RequestsNew = args[++i];
            break;
        case "--topic-requests-status" when i + 1 < args.Length:
            topics.RequestsStatus = args[++i];
            break;
        case "--topic-results-status" when i + 1 < args.Length:
            topics.ResultsStatus = args[++i];
            break;
        default:
            targetDir ??= args[i];
            break;
    }
}

targetDir ??= Path.Combine(RepoRoot(), "docs", "kafka-migration", "schemas", "current");
Directory.CreateDirectory(targetDir);

var pairs = new (string SubjectFile, string SchemaJson)[]
{
    ($"{topics.RequestsNew}-value.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{topics.RequestsStatus}-value.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{topics.ResultsStatus}-value.avsc", DorcEventSchemas.GenerateResultEventSchema())
};

foreach (var (file, json) in pairs)
{
    // Canonicalise (alphabetical key order) so the checked-in canonical and the
    // registry-acknowledged snapshot are human-diffable without spurious noise.
    var canonical = Canonicalise(JsonNode.Parse(json))!.ToJsonString();
    var path = Path.Combine(targetDir, file);
    File.WriteAllText(path, canonical);
    Console.WriteLine($"Wrote {path} ({canonical.Length} bytes)");
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
