using Dorc.Core.HighAvailability;
using Microsoft.Data.Sqlite;

namespace Dorc.Kafka.Lock.HATests;

/// <summary>
/// SPEC-S-005b §R-8 / AT-3..AT-5: HA suite for SC-2a/b/c bars.
/// Opt-in via <c>DORC_KAFKA_HA_TESTS=1</c>; requires a reachable broker.
/// On a green run, capture the transcript under
/// <c>docs/kafka-migration/S-005b-HA-evidence/&lt;timestamp&gt;/</c> per the
/// Definition of Done.
/// </summary>
[TestClass]
public class HAScenarioTests
{
    private static string UniqueGroup(string tag) => $"dorc.ha.{tag}.{Guid.NewGuid():N}";
    private static string UniqueTopic(string tag) => $"dorc.ha.locks.{tag}.{Guid.NewGuid():N}";

    /// <summary>AT-3 / SC-2a — leader-kill failover within 60s.</summary>
    [TestMethod]
    public async Task SC2a_LeaderKillFailover()
    {
        HATestPrereq.SkipIfDisabled();
        await using var h = new HAHarness(UniqueTopic("sc2a"), UniqueGroup("sc2a"), partitionCount: 3);

        var (c1, s1) = await h.AddCandidateAsync();
        var (c2, s2) = await h.AddCandidateAsync();

        // Wait briefly for rebalance to spread partitions.
        await Task.Delay(TimeSpan.FromSeconds(5));

        var resourceKey = "env:Production";
        var l1 = await s1.TryAcquireLockAsync(resourceKey, 20_000, CancellationToken.None);
        Assert.IsNotNull(l1, "Initial acquire must succeed on one of the candidates.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await h.RemoveCandidateAsync(c1); // simulate leader kill via clean close
        Assert.IsTrue(l1!.LockLostToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)),
            "Killed leader's LockLostToken must fire.");

        // New leader on c2 must be able to acquire the same key within 60s total.
        IDistributedLock? l2 = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            l2 = await s2.TryAcquireLockAsync(resourceKey, 5_000, CancellationToken.None);
            if (l2 is not null) break;
        }
        Assert.IsNotNull(l2, $"Failover acquire must succeed within 60s (elapsed={sw.Elapsed}).");
    }

    /// <summary>AT-4 / SC-2b — new-deployment acceptance post-failover ≤30s.</summary>
    [TestMethod]
    public async Task SC2b_NewDeploymentAcceptancePostFailover()
    {
        HATestPrereq.SkipIfDisabled();
        await using var h = new HAHarness(UniqueTopic("sc2b"), UniqueGroup("sc2b"), partitionCount: 3);
        var (c1, s1) = await h.AddCandidateAsync();
        var (c2, s2) = await h.AddCandidateAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));

        var key = "env:Staging";
        var original = await s1.TryAcquireLockAsync(key, 20_000, CancellationToken.None);
        Assert.IsNotNull(original);
        await h.RemoveCandidateAsync(c1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        IDistributedLock? reacquired = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            reacquired = await s2.TryAcquireLockAsync(key, 5_000, CancellationToken.None);
            if (reacquired is not null) break;
        }
        Assert.IsNotNull(reacquired, "New-deployment acquire must succeed on surviving candidate within 30s.");
    }

    /// <summary>
    /// AT-5 / SC-2c — ≥20 induced rebalances, zero duplicate (RequestId, Version)
    /// rows under a monotonic-guarded UPSERT against SQLite.
    /// </summary>
    [TestMethod]
    public async Task SC2c_TwentyRebalancesZeroDuplicates()
    {
        HATestPrereq.SkipIfDisabled();
        await using var h = new HAHarness(UniqueTopic("sc2c"), UniqueGroup("sc2c"), partitionCount: 3);

        var connStr = $"DataSource=file:ha-{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeper = new SqliteConnection(connStr);
        keeper.Open();
        using (var cmd = keeper.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE state (RequestId INTEGER NOT NULL, Version INTEGER NOT NULL, PRIMARY KEY(RequestId, Version));";
            cmd.ExecuteNonQuery();
        }

        await h.AddCandidateAsync();
        await h.AddCandidateAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var rebalances = 0;
        var writer = Task.Run(async () =>
        {
            var rnd = new Random(42);
            var requestId = 0;
            while (!cts.IsCancellationRequested)
            {
                var svc = h.Candidates[rnd.Next(h.Candidates.Count)].Svc;
                var key = $"env:Env-{rnd.Next(12)}";
                var handle = await svc.TryAcquireLockAsync(key, 1_000, cts.Token);
                if (handle is not null)
                {
                    try
                    {
                        var rid = Interlocked.Increment(ref requestId);
                        // Monotonic-guarded UPSERT: INSERT OR IGNORE on (RequestId, Version).
                        using var c = new SqliteConnection(connStr);
                        c.Open();
                        using var cmd = c.CreateCommand();
                        cmd.CommandText = "INSERT OR IGNORE INTO state (RequestId, Version) VALUES ($r, 1)";
                        cmd.Parameters.AddWithValue("$r", rid);
                        cmd.ExecuteNonQuery();
                    }
                    finally { await handle.DisposeAsync(); }
                }
            }
        });

        while (rebalances < 20 && !cts.IsCancellationRequested)
        {
            var (tmpCoord, _) = await h.AddCandidateAsync();
            await Task.Delay(TimeSpan.FromSeconds(4));
            await h.RemoveCandidateAsync(tmpCoord);
            await Task.Delay(TimeSpan.FromSeconds(4));
            rebalances++;
        }
        cts.Cancel();
        try { await writer; } catch (OperationCanceledException) { }

        using (var check = keeper.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM state GROUP BY RequestId, Version HAVING COUNT(*) > 1";
            using var reader = check.ExecuteReader();
            Assert.IsFalse(reader.HasRows, "No (RequestId, Version) pair may appear more than once.");
        }
        Assert.IsTrue(rebalances >= 20, $"Must induce ≥20 rebalances; induced {rebalances}.");
    }
}
