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

    /// <summary>
    /// Known-answer vectors from Apache Kafka's UtilsTest.testMurmur2 (signed
    /// int32 results of Java Utils.murmur2 with the same 0x9747b28c seed).
    /// Pins cross-language stability of resource-key → partition mapping.
    /// </summary>
    [TestMethod]
    public void Hash_MatchesApacheKafkaReferenceVectors()
    {
        static int Murmur2(byte[] bytes) => unchecked((int)MurmurHash2.Hash(bytes));

        Assert.AreEqual(-973932308, Murmur2(Encoding.UTF8.GetBytes("21")));
        Assert.AreEqual(-790332482, Murmur2(Encoding.UTF8.GetBytes("foobar")));
        Assert.AreEqual(-985981536, Murmur2(Encoding.UTF8.GetBytes("a-little-bit-long-string")));
        Assert.AreEqual(-1486304829, Murmur2(Encoding.UTF8.GetBytes("a-little-bit-longer-string")));
        Assert.AreEqual(-58897971, Murmur2(Encoding.UTF8.GetBytes("lkjh234lh9fiuh90y23oiuhsafujhadof229phr9h19h89h8")));
        Assert.AreEqual(479470107, Murmur2(new[] { (byte)'a', (byte)'b', (byte)'c' }));
    }

    /// <summary>
    /// Kafka maps hash → partition via toPositive(hash) % numPartitions where
    /// toPositive = hash &amp; 0x7fffffff. Pin the mask semantics on a
    /// negative-signed vector so the mapping helper can't drift.
    /// </summary>
    [TestMethod]
    public void PartitionMapping_UsesToPositiveMask()
    {
        var hash = MurmurHash2.Hash(Encoding.UTF8.GetBytes("foobar")); // -790332482 as signed int32
        var toPositive = (int)(hash & 0x7FFFFFFFu);
        Assert.AreEqual(-790332482 & 0x7fffffff, toPositive);
        Assert.IsTrue(toPositive >= 0);
    }
}
