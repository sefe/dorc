using Chr.Avro.Confluent;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Serialization;

/// <summary>
/// Chr.Avro-backed IKafkaSerializerFactory for the S-003 in-scope event
/// contracts. Returns Confluent ISerializer / IDeserializer instances for
/// the two <see cref="Dorc.Core.Events"/> records; returns null for every
/// other type so S-002's default-fallback semantics are preserved.
/// <para>
/// A single .NET type may be produced to multiple topics (e.g.
/// DeploymentRequestEventData → dorc.requests.new + dorc.requests.status).
/// The returned ISerializer&lt;T&gt; resolves its subject per-message via
/// Confluent TopicNameStrategy (topic + "-value"), lazily building and
/// caching a Chr.Avro schema-registry serializer per topic the first time
/// that topic is serialised to.
/// </para>
/// </summary>
public sealed class AvroKafkaSerializerFactory : IKafkaSerializerFactory, IDisposable
{
    private readonly ISchemaRegistryClient _registry;
    private readonly HashSet<Type> _inScope;
    private readonly List<IDisposable> _disposables = new();
    private readonly object _disposableLock = new();

    public AvroKafkaSerializerFactory(
        ISchemaRegistryClient registry,
        IOptions<KafkaAvroOptions>? options = null)
    {
        _registry = registry;
        _inScope = new HashSet<Type>
        {
            typeof(DeploymentRequestEventData),
            typeof(DeploymentResultEventData)
        };
        _ = options; // reserved for future subject-override use
    }

    public IReadOnlySet<Type> InScopeTypes => _inScope;

    public ISerializer<T>? GetKeySerializer<T>() => null;

    public IDeserializer<T>? GetKeyDeserializer<T>() => null;

    public ISerializer<T>? GetValueSerializer<T>()
    {
        if (!_inScope.Contains(typeof(T))) return null;
        var serializer = new TopicDispatchingSerializer<T>(_registry);
        lock (_disposableLock) _disposables.Add(serializer);
        return serializer;
    }

    public IDeserializer<T>? GetValueDeserializer<T>()
    {
        if (!_inScope.Contains(typeof(T))) return null;
        var deserializer = new TopicDispatchingDeserializer<T>(_registry);
        lock (_disposableLock) _disposables.Add(deserializer);
        return deserializer;
    }

    public void Dispose()
    {
        List<IDisposable> snapshot;
        lock (_disposableLock)
        {
            snapshot = new List<IDisposable>(_disposables);
            _disposables.Clear();
        }
        foreach (var d in snapshot)
        {
            try { d.Dispose(); } catch { /* best-effort */ }
        }
    }

    private sealed class TopicDispatchingSerializer<T> : ISerializer<T>, IDisposable
    {
        private readonly ISchemaRegistryClient _registry;
        private readonly Dictionary<string, ISerializer<T>> _byTopic = new();
        private readonly object _lock = new();

        public TopicDispatchingSerializer(ISchemaRegistryClient registry) => _registry = registry;

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var inner in _byTopic.Values)
                {
                    if (inner is IDisposable d)
                    {
                        try { d.Dispose(); } catch { /* best-effort */ }
                    }
                }
                _byTopic.Clear();
            }
        }

        public byte[] Serialize(T data, SerializationContext context)
        {
            var inner = GetOrCreate(context.Topic);
            return inner.Serialize(data, context);
        }

        private ISerializer<T> GetOrCreate(string topic)
        {
            lock (_lock)
            {
                if (_byTopic.TryGetValue(topic, out var existing)) return existing;

                var subject = topic + "-value";
                var serializer = new SchemaRegistrySerializerBuilder(_registry)
                    .Build<T>(subject, AutomaticRegistrationBehavior.Always, TombstoneBehavior.None)
                    .GetAwaiter()
                    .GetResult();
                _byTopic[topic] = serializer;
                return serializer;
            }
        }
    }

    private sealed class TopicDispatchingDeserializer<T> : IDeserializer<T>, IDisposable
    {
        private readonly ISchemaRegistryClient _registry;
        private readonly Dictionary<string, IDeserializer<T>> _byTopic = new();
        private readonly object _lock = new();

        public TopicDispatchingDeserializer(ISchemaRegistryClient registry) => _registry = registry;

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var inner in _byTopic.Values)
                {
                    if (inner is IDisposable d)
                    {
                        try { d.Dispose(); } catch { /* best-effort */ }
                    }
                }
                _byTopic.Clear();
            }
        }

        public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            var inner = GetOrCreate(context.Topic);
            return inner.Deserialize(data, isNull, context);
        }

        private IDeserializer<T> GetOrCreate(string topic)
        {
            lock (_lock)
            {
                if (_byTopic.TryGetValue(topic, out var existing)) return existing;

                var subject = topic + "-value";
                var deserializer = new SchemaRegistryDeserializerBuilder(_registry)
                    .Build<T>(subject, TombstoneBehavior.None)
                    .GetAwaiter()
                    .GetResult();
                _byTopic[topic] = deserializer;
                return deserializer;
            }
        }
    }
}
