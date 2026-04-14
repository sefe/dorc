using System.Text.Json.Nodes;
using Dorc.Kafka.Events;
using Dorc.Kafka.Events.Schemas;

static string RepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "src", "Dorc.sln"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("Could not locate repo root (src/Dorc.sln).");
}

var targetDir = args.Length > 0
    ? args[0]
    : Path.Combine(RepoRoot(), "docs", "kafka-migration", "schemas", "current");

Directory.CreateDirectory(targetDir);

var pairs = new (string SubjectFile, string SchemaJson)[]
{
    ($"{KafkaSubjectNames.RequestsNewValue}.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{KafkaSubjectNames.RequestsStatusValue}.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{KafkaSubjectNames.ResultsStatusValue}.avsc", DorcEventSchemas.GenerateResultEventSchema())
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
