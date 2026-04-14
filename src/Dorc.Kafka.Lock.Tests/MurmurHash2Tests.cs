using System.Text;

namespace Dorc.Kafka.Lock.Tests;

[TestClass]
public class MurmurHash2Tests
{
    [TestMethod]
    public void Hash_IsStableAcrossInvocations()
    {
        var input = Encoding.UTF8.GetBytes("env:Production");
        Assert.AreEqual(MurmurHash2.Hash(input), MurmurHash2.Hash(input));
    }

    [TestMethod]
    public void Hash_DiffersForDistinctInputs()
    {
        var a = MurmurHash2.Hash(Encoding.UTF8.GetBytes("env:Production"));
        var b = MurmurHash2.Hash(Encoding.UTF8.GetBytes("env:Staging"));
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Hash_DistributesAcrossPartitionCount()
    {
        // 1000 distinct keys across 12 partitions should hit every bucket.
        var hits = new HashSet<int>();
        for (var i = 0; i < 1000; i++)
        {
            var h = MurmurHash2.Hash(Encoding.UTF8.GetBytes($"env:Env-{i}"));
            hits.Add((int)((h & 0x7FFFFFFFu) % 12u));
        }
        Assert.AreEqual(12, hits.Count, "MurmurHash2 should distribute keys across all 12 partitions.");
    }
}
