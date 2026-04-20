using Dorc.Kafka.Events.SchemaGate;

// Usage: dotnet run -- [canonicalDir] [snapshotDir] [registryUrl]
// Defaults resolve relative to the repo root.

static string RepoRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "src", "Dorc.sln"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("Could not locate repo root (src/Dorc.sln).");
}

var repoRoot = RepoRoot();
var canonicalDir = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "docs", "kafka-migration", "schemas", "current");
var snapshotDir = args.Length > 1 ? args[1] : Path.Combine(repoRoot, "docs", "kafka-migration", "schemas", "latest");
var registryUrl = args.Length > 2
    ? args[2]
    : Environment.GetEnvironmentVariable("KAFKA_SCHEMA_REGISTRY") ?? "http://localhost:8081";

HttpClient? registryHttp = null;
var probeClient = new HttpClient
{
    BaseAddress = new Uri(registryUrl),
    Timeout = TimeSpan.FromSeconds(5)
};
try
{
    using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var probe = await probeClient.GetAsync("/subjects", probeCts.Token);
    if (probe.IsSuccessStatusCode)
    {
        registryHttp = probeClient;
        Console.WriteLine($"[schema-gate] Using live registry at {registryUrl}");
    }
    else
    {
        Console.WriteLine($"[schema-gate] Live registry at {registryUrl} returned {(int)probe.StatusCode}; falling back to snapshot at {snapshotDir}.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[schema-gate] Live registry at {registryUrl} unreachable ({ex.GetType().Name}); falling back to snapshot at {snapshotDir}.");
}
finally
{
    if (registryHttp is null)
        probeClient.Dispose();
}

var gate = new AvroSchemaGate(registryHttp, canonicalDir, Directory.Exists(snapshotDir) ? snapshotDir : null);
var reports = await gate.RunAsync();

var failed = false;
foreach (var report in reports)
{
    var prefix = report.Outcome == GateOutcome.Pass ? "PASS" : "FAIL";
    var src = report.Source is null ? "" : $" [{report.Source}]";
    Console.WriteLine($"[schema-gate] {prefix} {report.Subject}{src} — {report.Message}");
    if (report.Outcome == GateOutcome.Fail) failed = true;
}

registryHttp?.Dispose();
return failed ? 1 : 0;
