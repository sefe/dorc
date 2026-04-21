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

public sealed class RequestPollSignal : IRequestPollSignal, IDisposable
{
    private readonly SemaphoreSlim _sem = new(initialCount: 0, maxCount: 1);
    private volatile bool _disposed;

    public void Signal()
    {
        if (_disposed) return;
        try { _sem.Release(); }
        catch (SemaphoreFullException) { /* duplicate-collapse: already pending */ }
        catch (ObjectDisposedException) { /* race with Dispose: per-spec no-op */ }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            // After disposal, fall back to a plain delay so the loop continues
            // its baseline cadence until host shutdown completes.
            try { await Task.Delay(timeout, cancellationToken); } catch (OperationCanceledException) { }
            return;
        }
        try { await _sem.WaitAsync(timeout, cancellationToken); }
        catch (ObjectDisposedException) { /* race with Dispose */ }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _sem.Dispose(); } catch (ObjectDisposedException) { /* best-effort */ }
    }
}
