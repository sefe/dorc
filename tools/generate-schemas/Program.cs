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
    var arg = args[i];
    switch (arg)
    {
        case "--topic-requests-new":
        case "--topic-requests-status":
        case "--topic-results-status":
        case "--topic-requests-new-dlq":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                Console.Error.WriteLine($"[generate-schemas] error: flag {arg} requires a value");
                return 2;
            }
            switch (arg)
            {
                case "--topic-requests-new":     topics.RequestsNew     = args[++i]; break;
                case "--topic-requests-status":  topics.RequestsStatus  = args[++i]; break;
                case "--topic-results-status":   topics.ResultsStatus   = args[++i]; break;
                case "--topic-requests-new-dlq": topics.RequestsNewDlq  = args[++i]; break;
            }
            break;
        default:
            if (arg.StartsWith("--"))
            {
                Console.Error.WriteLine($"[generate-schemas] error: unknown flag {arg}");
                return 2;
            }
            targetDir ??= arg;
            break;
    }
}

targetDir ??= Path.Combine(RepoRoot(), "docs", "kafka-migration", "schemas", "current");
Directory.CreateDirectory(targetDir);

var pairs = new (string SubjectFile, string SchemaJson)[]
{
    ($"{topics.RequestsNew}-value.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{topics.RequestsStatus}-value.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{topics.ResultsStatus}-value.avsc", DorcEventSchemas.GenerateResultEventSchema()),
    ($"{topics.RequestsNewDlq}-value.avsc", DorcEventSchemas.GenerateErrorEnvelopeSchema())
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

return 0;

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
