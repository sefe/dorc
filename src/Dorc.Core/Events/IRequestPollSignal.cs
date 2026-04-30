namespace Dorc.Core.Events;

/// <summary>
/// Wake-up primitive that the S-006 Kafka request-lifecycle consumer raises and
/// the Monitor poll loop observes (SPEC-S-006 R-4). The contract:
/// <list type="bullet">
///   <item>The signal is <b>latchable across a "no waiter yet" window</b>:
///     <see cref="Signal"/> raised before any <see cref="WaitAsync"/> persists
///     until the next wait. Implemented as a <c>SemaphoreSlim(0, 1)</c> in the
///     production class.</item>
///   <item>Duplicate signals collapse to a single pending wake (max-count = 1).</item>
///   <item><see cref="WaitAsync"/> MUST observe the host cancellation token so
///     shutdown is not delayed by up to <c>iterationDelayMs</c>.</item>
///   <item><see cref="Signal"/> against a disposed/cancelled primitive is a
///     no-op — never throws, never logs an error, so the consumer loop can
///     never crash via this surface.</item>
/// </list>
/// </summary>
public interface IRequestPollSignal
{
    void Signal();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
