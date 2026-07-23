using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Configuration;

public sealed class KafkaTopicsOptionsValidator : IValidateOptions<KafkaTopicsOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaTopicsOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Locks))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.Locks)} is required.");

        if (string.IsNullOrWhiteSpace(options.RequestsNew))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.RequestsNew)} is required.");

        if (string.IsNullOrWhiteSpace(options.RequestsStatus))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.RequestsStatus)} is required.");

        if (string.IsNullOrWhiteSpace(options.ResultsStatus))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.ResultsStatus)} is required.");

        if (string.IsNullOrWhiteSpace(options.RequestsNewDlq))
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.RequestsNewDlq)} is required.");

        // Guard against copy-paste collisions: if any two topic names are
        // identical, a DLQ equal to its source topic creates an amplifying
        // error loop (poison message re-produced back onto the source topic).
        var names = new[]
        {
            (nameof(options.Locks), options.Locks),
            (nameof(options.RequestsNew), options.RequestsNew),
            (nameof(options.RequestsStatus), options.RequestsStatus),
            (nameof(options.ResultsStatus), options.ResultsStatus),
            (nameof(options.RequestsNewDlq), options.RequestsNewDlq)
        };

        var duplicates = names
            .Where(n => !string.IsNullOrWhiteSpace(n.Item2))
            .GroupBy(n => n.Item2, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(", ", g.Select(n => n.Item1)));

        foreach (var dup in duplicates)
            errors.Add($"{KafkaTopicsOptions.SectionName}: topic names must be distinct — duplicate value shared by: {dup}.");

        if (options.ReplicationFactor < 1)
            errors.Add($"{KafkaTopicsOptions.SectionName}:{nameof(KafkaTopicsOptions.ReplicationFactor)} must be >= 1.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
