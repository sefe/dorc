using Dorc.Core.HighAvailability;
using Microsoft.Data.Sqlite;

namespace Dorc.Kafka.Lock.HATests;

/// <summary>
/// HA suite covering leader-kill failover and rebalance robustness.
/// Opt-in via <c>DORC_KAFKA_HA_TESTS=1</c>; requires a reachable broker.
/// </summary>
[TestClass]
public class HAScenarioTests
{
    private static string UniqueGroup(string tag) => $"dorc.ha.{tag}.{Guid.NewGuid():N}";
    private static string UniqueTopic(string tag) => $"dorc.ha.locks.{tag}.{Guid.NewGuid():N}";

    /// <summary>
    /// The partition owning a key lands on whichever candidate the rebalance
    /// chose — initial acquire must therefore be attempted on every candidate,
    /// and the failover scenario kills whichever one actually holds the lock.
    /// </summary>
    private static async Task<(IDistributedLock Held, KafkaLockCoordinator Owner, KafkaDistributedLockService Survivor)>
        AcquireOnWhicheverCandidateOwnsAsync(
            string resourceKey,
            (KafkaLockCoordinator Coord, KafkaDistributedLockService Svc) c1,
            (KafkaLockCoordinator Coord, KafkaDistributedLockService Svc) c2)
    {
        var l1 = await c1.Svc.TryAcquireLockAsync(resourceKey, 0, CancellationToken.None);
        if (l1 is not null) return (l1, c1.Coord, c2.Svc);

        var l2 = await c2.Svc.TryAcquireLockAsync(resourceKey, 0, CancellationToken.None);
        Assert.IsNotNull(l2, "Initial acquire must succeed on one of the candidates.");
        return (l2!, c2.Coord, c1.Svc);
    }

    /// <summary>Leader-kill failover within 60s.</summary>
    [TestMethod]
    public async Task SC2a_LeaderKillFailover()
    {
        HATestPrereq.SkipIfDisabled();
        await using var h = new HAHarness(UniqueTopic("sc2a"), UniqueGroup("sc2a"), partitionCount: 3);
        await h.EnsureTopicAsync();

        var c1 = await h.AddCandidateAsync();
        var c2 = await h.AddCandidateAsync();

        // Wait for initial rebalance to spread partitions.
        await Task.Delay(TimeSpan.FromSeconds(15));

        var resourceKey = "env:Production";
        var (held, owner, survivorSvc) = await AcquireOnWhicheverCandidateOwnsAsync(resourceKey, c1, c2);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await h.RemoveCandidateAsync(owner); // simulate leader kill via clean close
        Assert.IsTrue(held.LockLostToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)),
            "Killed leader's LockLostToken must fire.");

        // The surviving candidate must be able to acquire the same key within 60s total.
        IDistributedLock? failover = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            failover = await survivorSvc.TryAcquireLockAsync(resourceKey, 0, CancellationToken.None);
            if (failover is not null) break;
        }
        Assert.IsNotNull(failover, $"Failover acquire must succeed within 60s (elapsed={sw.Elapsed}).");
    }

    /// <summary> / SC-2b — new-deployment acceptance post-failover ≤30s.</summary>
    [TestMethod]
    public async Task SC2b_NewDeploymentAcceptancePostFailover()
    {
        HATestPrereq.SkipIfDisabled();
        await using var h = new HAHarness(UniqueTopic("sc2b"), UniqueGroup("sc2b"), partitionCount: 3);
        await h.EnsureTopicAsync();
        var c1 = await h.AddCandidateAsync();
        var c2 = await h.AddCandidateAsync();
        await Task.Delay(TimeSpan.FromSeconds(15));

        var key = "env:Staging";
        var (_, owner, survivorSvc) = await AcquireOnWhicheverCandidateOwnsAsync(key, c1, c2);
        await h.RemoveCandidateAsync(owner);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        IDistributedLock? reacquired = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            reacquired = await survivorSvc.TryAcquireLockAsync(key, 0, CancellationToken.None);
            if (reacquired is not null) break;
        }
        Assert.IsNotNull(reacquired, "New-deployment acquire must succeed on surviving candidate within 30s.");
    }

    /// <summary>
    /// / SC-2c — ≥20 induced rebalances with concurrent writers on BOTH stable
    /// candidates performing read-modify-write against a SHARED per-key
    /// sequence, with zero duplicate (LockKey, Seq) rows.
    ///
    /// Mutual-exclusion assertion logic: each writer, while it believes it
    /// holds the lock for a key (handle.IsValid), reads MAX(Seq) for that key,
    /// deliberately dallies to widen the race window, then INSERTs Seq+1. The
    /// oplog table intentionally has NO primary key, so nothing suppresses a
    /// conflicting write: if mutual exclusion ever breaks (two candidates
    /// holding the same key simultaneously), both read the same MAX and both
    /// insert the same (LockKey, Seq) — the duplicate survives and the final
    /// GROUP BY ... HAVING COUNT(*) > 1 check fails the test. A single
    /// correctly-excluded writer can never produce a duplicate, and unlike the
    /// previous INSERT OR IGNORE on a per-writer unique id, this CAN fail.
    /// </summary>
    [TestMethod]
    public async Task SC2c_TwentyRebalancesZeroDuplicates()
    {
        HATestPrereq.SkipIfDisabled();
        await using var h = new HAHarness(UniqueTopic("sc2c"), UniqueGroup("sc2c"), partitionCount: 3);
        await h.EnsureTopicAsync();

        var connStr = $"DataSource=file:ha-{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeper = new SqliteConnection(connStr);
        keeper.Open();
        using (var cmd = keeper.CreateCommand())
        {
            // No PK on (LockKey, Seq): conflicting writes from a broken mutual
            // exclusion must land as duplicate rows, not be silently ignored.
            cmd.CommandText = "CREATE TABLE oplog (LockKey TEXT NOT NULL, Seq INTEGER NOT NULL, WriterId INTEGER NOT NULL);";
            cmd.ExecuteNonQuery();
        }

        var stable1 = await h.AddCandidateAsync();
        var stable2 = await h.AddCandidateAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // One concurrent writer per stable candidate (NOT a single sequential
        // writer hopping between services — both must contend for the same keys
        // at the same time for mutual exclusion to be exercised at all).
        var writers = new[] { (stable1, WriterId: 1), (stable2, WriterId: 2) }
            .Select(w => Task.Run(() => RunLockedReadModifyWriteLoopAsync(
                w.Item1.Svc, w.WriterId, connStr, cts.Token)))
            .ToArray();

        var rebalances = 0;
        while (rebalances < 20 && !cts.IsCancellationRequested)
        {
            var (tmpCoord, _) = await h.AddCandidateAsync();
            await Task.Delay(TimeSpan.FromSeconds(4));
            await h.RemoveCandidateAsync(tmpCoord);
            await Task.Delay(TimeSpan.FromSeconds(4));
            rebalances++;
        }
        cts.Cancel();
        await Task.WhenAll(writers); // writers swallow only our own cancellation

        using (var check = keeper.CreateCommand())
        {
            check.CommandText = "SELECT LockKey, Seq, COUNT(*) FROM oplog GROUP BY LockKey, Seq HAVING COUNT(*) > 1";
            using var reader = check.ExecuteReader();
            Assert.IsFalse(reader.HasRows,
                "Duplicate (LockKey, Seq) detected — two candidates performed the read-modify-write " +
                "for the same key concurrently: mutual exclusion is broken.");
        }

        using (var count = keeper.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM oplog";
            var rows = Convert.ToInt64(count.ExecuteScalar());
            Assert.IsGreaterThan(0L, rows, "Writers must have performed work for the duplicate check to be meaningful.");
        }

        Assert.IsGreaterThanOrEqualTo(20, rebalances, $"Must induce ≥20 rebalances; induced {rebalances}.");
    }

    /// <summary>
    /// Writer loop for SC2c: acquire a key's lock, then read-modify-write the
    /// shared per-key sequence only while the handle reports valid. The
    /// IsValid re-check immediately before the INSERT is best-effort fencing —
    /// cooperative rebalance fires the revoke (and LockLostToken) before the
    /// new owner is assigned, so abandoning the write on a lost lock closes
    /// the handoff window to milliseconds.
    /// </summary>
    private static async Task RunLockedReadModifyWriteLoopAsync(
        KafkaDistributedLockService svc, int writerId, string connStr, CancellationToken token)
    {
        var rnd = new Random(writerId * 1000 + 42);
        while (!token.IsCancellationRequested)
        {
            IDistributedLock? handle = null;
            try
            {
                var key = $"env:Env-{rnd.Next(4)}";
                handle = await svc.TryAcquireLockAsync(key, 0, token);
                if (handle is null) continue;

                if (!handle.IsValid) continue;

                long nextSeq;
                using (var read = new SqliteConnection(connStr))
                {
                    read.Open();
                    using var cmd = read.CreateCommand();
                    cmd.CommandText = "SELECT IFNULL(MAX(Seq), 0) + 1 FROM oplog WHERE LockKey = $k";
                    cmd.Parameters.AddWithValue("$k", key);
                    nextSeq = Convert.ToInt64(cmd.ExecuteScalar());
                }

                // Widen the race window: a competing holder reading the same MAX
                // in this gap will insert the same Seq and trip the duplicate check.
                await Task.Delay(25, token);

                if (!handle.IsValid) continue; // lock lost mid-operation → abandon the write

                using (var write = new SqliteConnection(connStr))
                {
                    write.Open();
                    using var cmd = write.CreateCommand();
                    cmd.CommandText = "INSERT INTO oplog (LockKey, Seq, WriterId) VALUES ($k, $s, $w)";
                    cmd.Parameters.AddWithValue("$k", key);
                    cmd.Parameters.AddWithValue("$s", nextSeq);
                    cmd.Parameters.AddWithValue("$w", writerId);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Expected: the scenario's explicit Cancel. Cancellations from
                // other sources still surface so unrelated failures aren't masked.
                return;
            }
            finally
            {
                if (handle is not null) await handle.DisposeAsync();
            }
        }
    }
}
