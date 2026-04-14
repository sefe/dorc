using Dorc.Kafka.Events;
using Dorc.Kafka.Events.Schemas;

var targetDir = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "kafka-migration", "schemas", "current");

Directory.CreateDirectory(targetDir);

var pairs = new (string SubjectFile, string SchemaJson)[]
{
    ($"{KafkaSubjectNames.RequestsNewValue}.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{KafkaSubjectNames.RequestsStatusValue}.avsc", DorcEventSchemas.GenerateRequestEventSchema()),
    ($"{KafkaSubjectNames.ResultsStatusValue}.avsc", DorcEventSchemas.GenerateResultEventSchema())
};

foreach (var (file, json) in pairs)
{
    var path = Path.Combine(targetDir, file);
    File.WriteAllText(path, json);
    Console.WriteLine($"Wrote {path} ({json.Length} bytes)");
}
