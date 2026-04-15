using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// SPEC-S-006 R-3 handler hook. Receives a <see cref="DeploymentRequestEventData"/>
/// pulled from <c>dorc.requests.new</c> or <c>dorc.requests.status</c> and
/// performs the acceleration-only side effect (raising the poll signal in
/// production). The consumer never executes requests through this handler;
/// it only signals the Monitor poll loop to wake up sooner.
/// </summary>
public interface IRequestEventHandler
{
    Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken);
}

public sealed class PollSignalRequestEventHandler : IRequestEventHandler
{
    private readonly IRequestPollSignal _signal;
    public PollSignalRequestEventHandler(IRequestPollSignal signal) => _signal = signal;

    public Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken)
    {
        _signal.Signal();
        return Task.CompletedTask;
    }
}
