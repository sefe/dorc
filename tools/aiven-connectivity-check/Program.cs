// One-shot Aiven connectivity probe — closes S-001 AT-4 / S-002 AT-7 / S-003 AT-7.
// Reads credentials from environment variables at runtime; never persists them.
//
// Required env vars:
//   AIVEN_BOOTSTRAP            e.g. traveler-unstable-dev-trading-traveler.e.aivencloud.com:26427
//   AIVEN_USER                 SASL username
//   AIVEN_PASSWORD             SASL password
//   AIVEN_CA_LOCATION          path to CA pem file
//   AIVEN_SCHEMA_REGISTRY_URL  e.g. https://traveler-unstable-dev-trading-traveler.e.aivencloud.com:26419
//                              (also uses AIVEN_USER / AIVEN_PASSWORD as basic-auth credentials)

using Confluent.Kafka;
using Confluent.SchemaRegistry;

string Req(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Env var {name} is required.");

var bootstrap = Req("AIVEN_BOOTSTRAP");
var user = Req("AIVEN_USER");
var password = Req("AIVEN_PASSWORD");
var caLocation = Req("AIVEN_CA_LOCATION");
var schemaRegistryUrl = Req("AIVEN_SCHEMA_REGISTRY_URL");

if (!File.Exists(caLocation))
    throw new FileNotFoundException($"CA file not found at {caLocation}");

Console.WriteLine("===== Aiven connectivity probe =====");
Console.WriteLine($"Bootstrap: {bootstrap}");
Console.WriteLine($"User:      {user}");
Console.WriteLine($"CA:        {caLocation}");
Console.WriteLine($"Registry:  {schemaRegistryUrl}");
Console.WriteLine();

// --- Step 1: Admin metadata fetch against the broker ---
Console.WriteLine("[1/2] Fetching cluster metadata via AdminClient...");
var adminConfig = new AdminClientConfig
{
    BootstrapServers = bootstrap,
    SecurityProtocol = SecurityProtocol.SaslSsl,
    SaslMechanism = Enum.TryParse<SaslMechanism>(Environment.GetEnvironmentVariable("AIVEN_SASL_MECH") ?? "ScramSha256", out var m) ? m : SaslMechanism.ScramSha256,
    SaslUsername = user,
    SaslPassword = password,
    SslCaLocation = caLocation
};
using (var admin = new AdminClientBuilder(adminConfig).Build())
{
    var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));
    Console.WriteLine($"  -> OK. Cluster id/name: {metadata.OriginatingBrokerName}");
    Console.WriteLine($"     Brokers: {metadata.Brokers.Count}");
    foreach (var broker in metadata.Brokers)
        Console.WriteLine($"       - id={broker.BrokerId} host={broker.Host}:{broker.Port}");
    Console.WriteLine($"     Topics visible to this user: {metadata.Topics.Count}");
}

// --- Step 2: Schema registry ping ---
Console.WriteLine();
Console.WriteLine("[2/2] Fetching subjects list via Schema Registry...");
var registryConfig = new SchemaRegistryConfig
{
    Url = schemaRegistryUrl,
    BasicAuthCredentialsSource = AuthCredentialsSource.UserInfo,
    BasicAuthUserInfo = $"{user}:{password}",
    SslCaLocation = caLocation
};
using (var registry = new CachedSchemaRegistryClient(registryConfig))
{
    var subjects = await registry.GetAllSubjectsAsync();
    Console.WriteLine($"  -> OK. Subjects: {subjects.Count}");
    foreach (var subject in subjects.Take(10))
        Console.WriteLine($"       - {subject}");
    if (subjects.Count > 10) Console.WriteLine($"       ... and {subjects.Count - 10} more");
}

Console.WriteLine();
Console.WriteLine("===== Probe complete — all checks passed =====");
