namespace Dorc.Kafka.Lock;

/// <summary>
/// Kafka's MurmurHash2 partitioner hash (seed 0x9747b28c). Matches
/// <c>Utils.murmur2</c> in the Apache Kafka Java client (the Java producer's
/// default partitioner) and librdkafka's
/// <c>rd_kafka_msg_partitioner_murmur2_random</c> partitioner.
///
/// NOTE: this is NOT librdkafka's default partitioner —
/// <c>rd_kafka_msg_partitioner_consistent_random</c>, the librdkafka default,
/// is CRC32-based and produces a different key → partition mapping. Any
/// producer that keys records into <c>dorc.locks</c> must set
/// <c>Partitioner=Murmur2Random</c> so its partitioning aligns with
/// <c>KafkaLockCoordinator.GetPartitionFor</c>.
/// </summary>
internal static class MurmurHash2
{
    public static uint Hash(ReadOnlySpan<byte> data)
    {
        const uint seed = 0x9747b28c;
        const uint m = 0x5bd1e995;
        const int r = 24;

        int length = data.Length;
        uint h = seed ^ (uint)length;

        int i = 0;
        while (length >= 4)
        {
            uint k = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
            k *= m;
            k ^= k >> r;
            k *= m;
            h *= m;
            h ^= k;
            i += 4;
            length -= 4;
        }

        switch (length)
        {
            case 3:
                h ^= (uint)(data[i + 2] << 16);
                goto case 2;
            case 2:
                h ^= (uint)(data[i + 1] << 8);
                goto case 1;
            case 1:
                h ^= data[i];
                h *= m;
                break;
        }

        h ^= h >> 13;
        h *= m;
        h ^= h >> 15;

        return h;
    }
}
