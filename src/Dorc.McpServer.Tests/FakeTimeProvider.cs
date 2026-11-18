namespace Dorc.McpServer.Tests;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
