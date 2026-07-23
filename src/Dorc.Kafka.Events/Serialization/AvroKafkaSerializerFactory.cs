using Chr.Avro.Confluent;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Dorc.Core.Events;
using Dorc.Kafka.Client;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _serializers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _deserializers = new();
    private readonly ILogger _logger;
    private readonly AutomaticRegistrationBehavior _registrationBehavior;

    public AvroKafkaSerializerFactory(
        ISchemaRegistryClient registry,
        IOptions<KafkaAvroOptions>? options = null,
        ILogger<AvroKafkaSerializerFactory>? logger = null)
    {
        _registry = registry;
        _inScope = new HashSet<Type>
        {
            typeof(DeploymentRequestEventData),
            typeof(DeploymentResultEventData),
            typeof(Dorc.Kafka.ErrorLog.KafkaErrorEnvelope)
        };
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _registrationBehavior = (options?.Value.AllowAutomaticSchemaRegistration ?? false)
            ? AutomaticRegistrationBehavior.Always
            : AutomaticRegistrationBehavior.Never;
    }

    public IReadOnlySet<Type> InScopeTypes => _inScope;

    /// <summary>
    /// Registration behaviour passed to every Chr.Avro serializer this
    /// factory builds. <c>internal</c> test seam pinning the
    /// <see cref="KafkaAvroOptions.AllowAutomaticSchemaRegistration"/>
    /// flow-through: the default (<see cref="AutomaticRegistrationBehavior.Never"/>)
    /// means a producer with registry write credentials cannot register an
    /// evolved schema on first publish and bypass the PR-time schema gate.
    /// </summary>
    internal AutomaticRegistrationBehavior RegistrationBehavior => _registrationBehavior;

    public ISerializer<T>? GetKeySerializer<T>() => null;

    public IDeserializer<T>? GetKeyDeserializer<T>() => null;

    public ISerializer<T>? GetValueSerializer<T>()
    {
        if (!_inScope.Contains(typeof(T))) return null;
        return (TopicDispatchingSerializer<T>)_serializers.GetOrAdd(
            typeof(T),
            _ => new TopicDispatchingSerializer<T>(_registry, _logger, _registrationBehavior));
    }

    public IDeserializer<T>? GetValueDeserializer<T>()
    {
        if (!_inScope.Contains(typeof(T))) return null;
        return (TopicDispatchingDeserializer<T>)_deserializers.GetOrAdd(
            typeof(T),
            _ => new TopicDispatchingDeserializer<T>(_registry, _logger));
    }

    public void Dispose()
    {
        foreach (var s in _serializers.Values.OfType<IDisposable>())
        {
            try { s.Dispose(); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
            {
                _logger.LogWarning(ex, "avro-dispose-failed inner={InnerType}", s.GetType().Name);
            }
        }
        foreach (var d in _deserializers.Values.OfType<IDisposable>())
        {
            try { d.Dispose(); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
            {
                _logger.LogWarning(ex, "avro-dispose-failed inner={InnerType}", d.GetType().Name);
            }
        }
        _serializers.Clear();
        _deserializers.Clear();
    }

    /// <summary>
    /// Pre-builds the deserializer for <typeparamref name="T"/> against each
    /// supplied topic so the first <c>Consume()</c> call doesn't pay the
    /// schema-registry round-trip on the consume thread (where exceeding
    /// max.poll.interval.ms would fence the consumer and trigger a group
    /// rebalance). Idempotent. A registry failure on any single topic is
    /// logged at Warning; the per-message lazy path remains the safety net.
    /// </summary>
    public void WarmupDeserializer<T>(IEnumerable<string> topics)
    {
        if (!_inScope.Contains(typeof(T))) return;
        var d = (TopicDispatchingDeserializer<T>)_deserializers.GetOrAdd(
            typeof(T),
            _ => new TopicDispatchingDeserializer<T>(_registry, _logger));
        foreach (var topic in topics)
        {
            try { d.Prebuild(topic); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
            {
                _logger.LogWarning(ex,
                    "avro-warmup-failed kind=deserializer topic={Topic} type={Type}",
                    topic, typeof(T).Name);
            }
        }
    }

    /// <summary>
    /// Producer-side counterpart to <see cref="WarmupDeserializer{T}"/>. Useful
    /// for publisher startup where the first publish to an unfamiliar topic
    /// would otherwise block. Idempotent; failures logged at Warning.
    /// </summary>
    public void WarmupSerializer<T>(IEnumerable<string> topics)
    {
        if (!_inScope.Contains(typeof(T))) return;
        var s = (TopicDispatchingSerializer<T>)_serializers.GetOrAdd(
            typeof(T),
            _ => new TopicDispatchingSerializer<T>(_registry, _logger, _registrationBehavior));
        foreach (var topic in topics)
        {
            try { s.Prebuild(topic); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
            {
                _logger.LogWarning(ex,
                    "avro-warmup-failed kind=serializer topic={Topic} type={Type}",
                    topic, typeof(T).Name);
            }
        }
    }

    private sealed class TopicDispatchingSerializer<T> : ISerializer<T>, IDisposable
    {
        private readonly ISchemaRegistryClient _registry;
        private readonly ILogger _logger;
        private readonly AutomaticRegistrationBehavior _registrationBehavior;
        private readonly Dictionary<string, ISerializer<T>> _byTopic = new();
        private readonly object _lock = new();

        public TopicDispatchingSerializer(
            ISchemaRegistryClient registry,
            ILogger logger,
            AutomaticRegistrationBehavior registrationBehavior)
        {
            _registry = registry;
            _logger = logger;
            _registrationBehavior = registrationBehavior;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var d in _byTopic.Values.OfType<IDisposable>())
                {
                    try { d.Dispose(); }
                    catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
                    {
                        _logger.LogWarning(ex, "avro-serializer-dispose-failed type={Type}", typeof(T).Name);
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

        public void Prebuild(string topic) => GetOrCreate(topic);

        private ISerializer<T> GetOrCreate(string topic)
        {
            // Fast path: cache hit under read-only lock acquisition.
            lock (_lock)
            {
                if (_byTopic.TryGetValue(topic, out var existing)) return existing;
            }

            // Build the schema-registry serializer OUTSIDE the lock so a slow
            // registry round-trip can't stall the consume thread holding it
            // (and from there fence the consumer group via max.poll.interval).
            // A concurrent caller may build the same subject twice; we
            // de-duplicate at insert time and dispose the loser.
            var subject = topic + "-value";
            using var builder = new SchemaRegistrySerializerBuilder(_registry);
            var built = builder
                .Build<T>(subject, _registrationBehavior, TombstoneBehavior.None)
                .GetAwaiter()
                .GetResult();

            lock (_lock)
            {
                if (_byTopic.TryGetValue(topic, out var existing))
                {
                    if (built is IDisposable d) d.Dispose();
                    return existing;
                }
                _byTopic[topic] = built;
            }

            _logger.LogInformation(
                "avro-schema-resolved subject={Subject} type={Type} kind=serializer",
                subject, typeof(T).Name);
            return built;
        }
    }

    private sealed class TopicDispatchingDeserializer<T> : IDeserializer<T>, IDisposable
    {
        private readonly ISchemaRegistryClient _registry;
        private readonly ILogger _logger;
        private readonly Dictionary<string, IDeserializer<T>> _byTopic = new();
        private readonly object _lock = new();

        public TopicDispatchingDeserializer(ISchemaRegistryClient registry, ILogger logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var d in _byTopic.Values.OfType<IDisposable>())
                {
                    try { d.Dispose(); }
                    catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
                    {
                        _logger.LogWarning(ex, "avro-deserializer-dispose-failed type={Type}", typeof(T).Name);
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

        public void Prebuild(string topic) => GetOrCreate(topic);

        private IDeserializer<T> GetOrCreate(string topic)
        {
            lock (_lock)
            {
                if (_byTopic.TryGetValue(topic, out var existing)) return existing;
            }

            var subject = topic + "-value";
            using var builder = new SchemaRegistryDeserializerBuilder(_registry);
            var built = builder
                .Build<T>(subject, TombstoneBehavior.None)
                .GetAwaiter()
                .GetResult();

            lock (_lock)
            {
                if (_byTopic.TryGetValue(topic, out var existing))
                {
                    if (built is IDisposable d) d.Dispose();
                    return existing;
                }
                _byTopic[topic] = built;
            }

            _logger.LogInformation(
                "avro-schema-resolved subject={Subject} type={Type} kind=deserializer",
                subject, typeof(T).Name);
            return built;
        }
    }
}
