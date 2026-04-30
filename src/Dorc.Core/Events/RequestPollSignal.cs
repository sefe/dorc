namespace Dorc.Core.Events;

public sealed class RequestPollSignal : IRequestPollSignal, IDisposable
{
    private readonly SemaphoreSlim _sem = new(initialCount: 0, maxCount: 1);
    private volatile bool _disposed;

    public void Signal()
    {
        if (_disposed) return;
        try { _sem.Release(); }
        catch (SemaphoreFullException)
        {
            // Duplicate-collapse: a Signal is already pending (max-count = 1).
            // Per IRequestPollSignal contract: "Duplicate signals collapse to a
            // single pending wake" — swallowing here is the documented behaviour.
        }
        catch (ObjectDisposedException)
        {
            // Race with Dispose. Per IRequestPollSignal contract: "Signal against
            // a disposed/cancelled primitive is a no-op — never throws, never
            // logs an error, so the consumer loop can never crash via this
            // surface."
        }
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
        catch (ObjectDisposedException)
        {
            // Race with Dispose — same no-throw / no-log contract as Signal.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _sem.Dispose(); }
        catch (ObjectDisposedException)
        {
            // Already disposed by another caller; idempotent best-effort.
        }
    }
}
