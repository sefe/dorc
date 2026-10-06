namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> for the coordinator's connectivity
/// watchdog tests: timestamps only move when a test advances them.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestampTicks;

    public override long GetTimestamp() => Interlocked.Read(ref _timestampTicks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _timestampTicks, by.Ticks);
}
