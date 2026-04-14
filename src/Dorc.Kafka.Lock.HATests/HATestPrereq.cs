namespace Dorc.Kafka.Lock.HATests;

/// <summary>
/// Gate for SPEC-S-005b R-8 HA tests. Opt-in via <c>DORC_KAFKA_HA_TESTS=1</c>
/// so CI default stays fast. Also reads bootstrap / credentials from env so
/// the same suite runs against compose or Aiven non-prod.
/// </summary>
internal static class HATestPrereq
{
    public static bool Enabled => Environment.GetEnvironmentVariable("DORC_KAFKA_HA_TESTS") == "1";
    public static string Bootstrap => Environment.GetEnvironmentVariable("DORC_KAFKA_BOOTSTRAP") ?? "localhost:9092";
    public static string? SaslUser => Environment.GetEnvironmentVariable("DORC_KAFKA_SASL_USER");
    public static string? SaslPass => Environment.GetEnvironmentVariable("DORC_KAFKA_SASL_PASS");
    public static string? SaslCa => Environment.GetEnvironmentVariable("DORC_KAFKA_CA_PEM");

    public static void SkipIfDisabled()
    {
        if (!Enabled)
            Assert.Inconclusive("HA tests disabled. Set DORC_KAFKA_HA_TESTS=1 with a reachable broker to run.");
    }
}
