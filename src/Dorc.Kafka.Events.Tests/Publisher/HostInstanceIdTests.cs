using Dorc.Kafka.Events.Publisher;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Per-replica consumer-group identity must be unique across replicas AND
/// stable across restarts. These tests pin the resolution rules so a future
/// change can't silently break either invariant.
/// </summary>
[TestClass]
public class HostInstanceIdTests
{
    [TestMethod]
    public void Resolve_PrefersDorcReplicaIdEnvVarOverPid()
    {
        // Operator-supplied identity (recommended for K8s pod UID via the
        // downward API) wins over the PID fallback.
        var result = HostInstanceId.Resolve(
            env: name => name == HostInstanceId.EnvironmentVariable ? "abc-123" : null,
            machineName: "host01",
            processId: 999);

        Assert.AreEqual("host01-abc-123", result);
    }

    [TestMethod]
    public void Resolve_TrimsConfiguredValue()
    {
        var result = HostInstanceId.Resolve(
            env: _ => "  pod-uid  ",
            machineName: "host01",
            processId: 999);

        Assert.AreEqual("host01-pod-uid", result);
    }

    [TestMethod]
    public void Resolve_TreatsWhitespaceConfiguredValueAsUnset()
    {
        var result = HostInstanceId.Resolve(
            env: _ => "   ",
            machineName: "host01",
            processId: 999);

        Assert.AreEqual("host01-999", result);
    }

    [TestMethod]
    public void Resolve_FallsBackToMachineNamePlusPidWhenEnvUnset()
    {
        // Without DORC_REPLICA_ID, two processes on the same host must get
        // different identities (PID-suffixed) — otherwise they'd join the
        // same consumer group and the per-replica fan-out invariant breaks.
        var first = HostInstanceId.Resolve(env: _ => null, machineName: "host01", processId: 1234);
        var second = HostInstanceId.Resolve(env: _ => null, machineName: "host01", processId: 5678);

        Assert.AreEqual("host01-1234", first);
        Assert.AreEqual("host01-5678", second);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void Resolve_DistinguishesHostsWithSamePid()
    {
        var a = HostInstanceId.Resolve(env: _ => null, machineName: "host01", processId: 100);
        var b = HostInstanceId.Resolve(env: _ => null, machineName: "host02", processId: 100);

        Assert.AreNotEqual(a, b);
    }
}
