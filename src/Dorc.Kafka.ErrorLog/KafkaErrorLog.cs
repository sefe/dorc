using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog;

public sealed class KafkaErrorLog : IKafkaErrorLog
{
    private readonly IKafkaErrorLogContextFactory _contextFactory;
    private readonly KafkaErrorLogOptions _options;

    public KafkaErrorLog(IKafkaErrorLogContextFactory contextFactory, IOptions<KafkaErrorLogOptions> options)
    {
        _contextFactory = contextFactory;
        _options = options.Value;
    }

    public async Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
    {
        ApplyPayloadTruncation(entry, _options.MaxPayloadBytes);
        entry.LoggedAt = DateTimeOffset.UtcNow;

        using var context = _contextFactory.GetContext();
        context.KafkaErrorLogEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(
        string? topic,
        string? consumerGroup,
        DateTimeOffset? sinceUtc,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var bounded = Math.Min(maxRows, _options.QueryMaxRowsCap);
        if (bounded <= 0) return Array.Empty<KafkaErrorLogEntry>();

        using var context = _contextFactory.GetContext();
        IQueryable<KafkaErrorLogEntry> query = context.KafkaErrorLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(topic))
            query = query.Where(e => e.Topic == topic);
        if (!string.IsNullOrWhiteSpace(consumerGroup))
            query = query.Where(e => e.ConsumerGroup == consumerGroup);
        if (sinceUtc.HasValue)
        {
            var since = sinceUtc.Value;
            query = query.Where(e => e.OccurredAt >= since);
        }

        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(bounded)
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(_options.RetentionDays);
        var totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var context = _contextFactory.GetContext();
            var batch = await context.KafkaErrorLogEntries
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.Id)
                .Take(_options.PurgeBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) break;

            context.KafkaErrorLogEntries.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            if (batch.Count < _options.PurgeBatchSize) break;
        }

        return totalDeleted;
    }

    internal static void ApplyPayloadTruncation(KafkaErrorLogEntry entry, int maxBytes)
    {
        if (entry.RawPayload is null)
        {
            entry.PayloadTruncated = false;
            return;
        }

        if (entry.RawPayload.Length > maxBytes)
        {
            var truncated = new byte[maxBytes];
            Buffer.BlockCopy(entry.RawPayload, 0, truncated, 0, maxBytes);
            entry.RawPayload = truncated;
            entry.PayloadTruncated = true;
        }
        else
        {
            entry.PayloadTruncated = false;
        }
    }
}
