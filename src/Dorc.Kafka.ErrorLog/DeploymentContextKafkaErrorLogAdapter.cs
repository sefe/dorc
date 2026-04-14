using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Kafka.ErrorLog;

/// <summary>
/// Adapts the production <see cref="IDeploymentContextFactory"/> to the
/// narrow <see cref="IKafkaErrorLogContextFactory"/> the DAL depends on.
/// </summary>
public sealed class DeploymentContextKafkaErrorLogContextFactory : IKafkaErrorLogContextFactory
{
    private readonly IDeploymentContextFactory _inner;

    public DeploymentContextKafkaErrorLogContextFactory(IDeploymentContextFactory inner)
    {
        _inner = inner;
    }

    public IKafkaErrorLogContext GetContext() => new Adapter(_inner.GetContext());

    private sealed class Adapter : IKafkaErrorLogContext
    {
        private readonly IDeploymentContext _inner;

        public Adapter(IDeploymentContext inner) => _inner = inner;

        public DbSet<KafkaErrorLogEntry> KafkaErrorLogEntries => _inner.Set<KafkaErrorLogEntry>();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
            => _inner.SaveChangesAsync(cancellationToken);

        public void Dispose() => _inner.Dispose();
    }
}
