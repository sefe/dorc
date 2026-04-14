using Dorc.Kafka.ErrorLog;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Kafka.ErrorLog.Tests;

/// <summary>
/// Wires the production <see cref="KafkaErrorLog"/> against the EF Core
/// In-Memory provider. Each harness gets its own database id so tests are
/// hermetic. SQL-Server-specific column type hints in the production
/// EntityTypeConfiguration are silently ignored by the In-Memory provider,
/// which is fine for testing logical query/insert/purge behaviour. Schema
/// fidelity (column types, indexes) is the job of AT-3, which lives in a
/// separate integration test project against a real SQL Server.
/// </summary>
internal sealed class InMemoryTestHarness : IDisposable
{
    private readonly string _databaseName;

    public InMemoryTestHarness(KafkaErrorLogOptions? options = null)
    {
        _databaseName = $"kafka-error-log-{Guid.NewGuid():N}";
        Options = options ?? new KafkaErrorLogOptions();
        Factory = new InMemoryContextFactory(_databaseName);
        DAL = new KafkaErrorLog(Factory, Microsoft.Extensions.Options.Options.Create(Options));
    }

    public KafkaErrorLogOptions Options { get; }
    public IKafkaErrorLogContextFactory Factory { get; }
    public IKafkaErrorLog DAL { get; }

    public InMemoryContext NewContext() => (InMemoryContext)Factory.GetContext();

    public void Dispose() { /* In-Memory provider GC-collects when the last reference drops */ }
}

internal sealed class InMemoryContextFactory : IKafkaErrorLogContextFactory
{
    private readonly string _databaseName;
    public InMemoryContextFactory(string databaseName) => _databaseName = databaseName;
    public IKafkaErrorLogContext GetContext() => new InMemoryContext(_databaseName);
}

internal sealed class InMemoryContext : DbContext, IKafkaErrorLogContext
{
    private readonly string _databaseName;

    public InMemoryContext(string databaseName) => _databaseName = databaseName;

    public DbSet<KafkaErrorLogEntry> KafkaErrorLogEntries { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseInMemoryDatabase(_databaseName)
                  .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply only the columns we test against. The production
        // EntityTypeConfiguration sets HasColumnType("datetimeoffset(7)") /
        // HasColumnType("varbinary(max)") which the In-Memory provider
        // simply ignores; that's fine here because schema fidelity is AT-3's
        // job (real SQL Server). We still apply the production config so
        // any non-type aspects (HasMaxLength, IsRequired) are honoured.
        new Dorc.PersistentData.EntityTypeConfigurations.KafkaErrorLogEntryEntityTypeConfiguration()
            .Configure(modelBuilder.Entity<KafkaErrorLogEntry>());
    }
}
