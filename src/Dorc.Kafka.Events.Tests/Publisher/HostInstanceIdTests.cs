using Dorc.Kafka.Events.Publisher;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Per-replica consumer-group identity must be unique across replicas AND
/// stable across restarts. These tests pin the resolution rules so a future
/// change can't silently break either invariant. In particular the fallback
/// must NOT contain anything restart-volatile (e.g. a process id): a fresh
/// suffix per restart mints a new consumer group every time — orphan groups
/// accumulate in __consumer_offsets and, with AutoOffsetReset.Earliest on
/// the requests consumer, the whole retained topic replays on each restart.
/// </summary>
[TestClass]
public class HostInstanceIdTests
{
    [TestMethod]
    public void Resolve_PrefersDorcReplicaIdEnvVarOverFallback()
    {
        // Operator-supplied identity (recommended for K8s pod UID via the
        // downward API; REQUIRED for multi-replica-per-host deployments)
        // wins over the machine-name fallback.
        var result = HostInstanceId.Resolve(
            env: name => name == HostInstanceId.EnvironmentVariable ? "abc-123" : null,
            machineName: "host01");

        Assert.AreEqual("host01-abc-123", result);
    }

    [TestMethod]
    public void Resolve_TrimsConfiguredValue()
    {
        var result = HostInstanceId.Resolve(
            env: _ => "  pod-uid  ",
            machineName: "host01");

        Assert.AreEqual("host01-pod-uid", result);
    }

    [TestMethod]
    public void Resolve_TreatsWhitespaceConfiguredValueAsUnset()
    {
        var result = HostInstanceId.Resolve(
            env: _ => "   ",
            machineName: "host01");

        Assert.AreEqual("host01", result);
    }

    [TestMethod]
    public void Resolve_FallbackIsMachineNameAlone_StableAcrossRestarts()
    {
        // Two resolutions for the same host (i.e. the same replica before and
        // after a process restart) MUST yield the same identity, otherwise
        // every restart joins a brand-new consumer group.
        var beforeRestart = HostInstanceId.Resolve(env: _ => null, machineName: "host01");
        var afterRestart = HostInstanceId.Resolve(env: _ => null, machineName: "host01");

        Assert.AreEqual("host01", beforeRestart);
        Assert.AreEqual(beforeRestart, afterRestart);
    }

    [TestMethod]
    public void Resolve_FallbackDistinguishesHosts()
    {
        var a = HostInstanceId.Resolve(env: _ => null, machineName: "host01");
        var b = HostInstanceId.Resolve(env: _ => null, machineName: "host02");

        Assert.AreNotEqual(a, b);
    }
}
