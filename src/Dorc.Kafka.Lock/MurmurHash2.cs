namespace Dorc.Kafka.Lock;

/// <summary>
/// librdkafka / Java-Kafka default partitioner hash (MurmurHash2, seed 0x9747b28c).
/// Matches <c>Utils.murmur2</c> in the Apache Kafka client and the
/// <c>rd_kafka_msg_partitioner_consistent_random</c> path in librdkafka, so
/// resource-key → partition mapping is stable across languages and brokers.
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
