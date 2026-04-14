using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Kafka.ErrorLog;

/// <summary>
/// Narrow EF Core surface the DAL needs. The production
/// <c>DeploymentContext</c> implements this trivially via its existing
/// KafkaErrorLogEntries DbSet + SaveChangesAsync. Defined separately so
/// tests can stand up a minimal SQLite-backed context without retrofitting
/// the full DOrc model.
/// </summary>
public interface IKafkaErrorLogContext : IDisposable
{
    DbSet<KafkaErrorLogEntry> KafkaErrorLogEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IKafkaErrorLogContextFactory
{
    IKafkaErrorLogContext GetContext();
}
