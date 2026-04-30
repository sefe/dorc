namespace Dorc.Kafka.Events.Configuration;

/// <summary>
/// Single canonical home for the four Kafka topic names the DOrc substrate
/// produces to / consumes from (SPEC-S-017). Defaults preserve the historical
/// hard-coded values so a fresh dev clone runs against a local broker without
/// any per-environment override; per-environment topic names are surfaced via
/// MSI properties + WiX writes in `Setup.Dorc.msi.json` / `RequestApi.wxs` /
/// the Monitor `.wxs` files (SPEC-S-017 R4).
///
/// Validator (<see cref="KafkaTopicsOptionsValidator"/>) enforces non-empty
/// only — no character-set / length / format restriction. SEFE topic names
/// (e.g. <c>tr.dv.gbl.deploy.request.il2.dorc</c>) deviate from the
/// <c>dorc.*</c> default pattern and any tighter validator would reject them.
/// </summary>
public sealed class KafkaTopicsOptions
{
    public const string SectionName = "Kafka:Topics";

    public string Locks { get; set; } = "dorc.locks";
    public string RequestsNew { get; set; } = "dorc.requests.new";
    public string RequestsStatus { get; set; } = "dorc.requests.status";
    public string ResultsStatus { get; set; } = "dorc.results.status";

    /// <summary>
    /// DLQ topic for poison messages received on <see cref="RequestsNew"/>.
    /// Per K-2 resolution: the DLQ tier is enabled only for the new-request
    /// topic; <see cref="RequestsStatus"/> and <see cref="ResultsStatus"/>
    /// poison messages fall straight through to the structured-log tier.
    /// </summary>
    public string RequestsNewDlq { get; set; } = "dorc.requests.new.dlq";
}
