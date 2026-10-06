using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Drives the REAL <see cref="KafkaConsumeFailureRecorder"/> — the shared
/// collaborator both consumers (<c>DeploymentRequestsKafkaConsumer</c> and
/// <c>DeploymentResultsKafkaConsumer</c>) build from their ctor deps for the
/// three-tier failure contract: DLQ insert (timeout linked to the stopping
/// token) → structured-log fallback (with bounded payload) → super-degraded
/// swallow. Previously this file tested a private COPY of the consumers'
/// try/catch shape, which could silently drift from production; extracting
/// the collaborator removed the copy.
/// </summary>
[TestClass]
public class WriteErrorLogSuperDegradedTests
{
    [TestMethod]
    public void Record_DlqInsertSucceeds_NoFallbackLogged()
    {
        var errorLog = new RecordingErrorLog();
        var logger = new CapturingLogger();
        var sut = new KafkaConsumeFailureRecorder(errorLog, logger);
        var entry = NewEntry();

        sut.Record(entry, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.AreSame(entry, errorLog.LastEntry, "entry must flow to IKafkaErrorLog.InsertAsync unmodified");
        var errorLogged = logger.Entries.Single(e => e.Level == LogLevel.Error);
        StringAssert.Contains(errorLogged.Message, "error-logged");
        Assert.IsFalse(logger.Entries.Any(e => e.Message.Contains("error-fallback-structured-log")),
            "fallback tier must not run when the DLQ insert succeeds");
    }

    [TestMethod]
    public void Record_DlqThrows_WritesStructuredFallbackWithPayloadBase64()
    {
        var errorLog = new ThrowingErrorLog();
        var logger = new CapturingLogger();
        var sut = new KafkaConsumeFailureRecorder(errorLog, logger);
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var entry = NewEntry(rawPayload: payload, exceptionType: "System.FormatException");

        sut.Record(entry, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.IsTrue(errorLog.WasCalled);
        var fallback = logger.Entries.Single(e => e.Message.Contains("error-fallback-structured-log"));
        Assert.AreEqual(LogLevel.Error, fallback.Level);
        Assert.IsNotNull(fallback.Exception, "DAL exception must be attached to the fallback log");
        StringAssert.Contains(fallback.Message, Convert.ToBase64String(payload),
            "fallback log must carry the payload — once the offset advances the content is otherwise unrecoverable");
        StringAssert.Contains(fallback.Message, "System.FormatException",
            "fallback log must carry the failure exception type");
    }

    [TestMethod]
    public void Record_DlqThrows_PayloadOverCap_TruncatedWithExplicitMarker()
    {
        var errorLog = new ThrowingErrorLog();
        var logger = new CapturingLogger();
        var sut = new KafkaConsumeFailureRecorder(errorLog, logger);
        var payload = new byte[KafkaConsumeFailureRecorder.FallbackLogPayloadCapBytes + 100];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251);
        var entry = NewEntry(rawPayload: payload);

        sut.Record(entry, TimeSpan.FromSeconds(5), CancellationToken.None);

        var fallback = logger.Entries.Single(e => e.Message.Contains("error-fallback-structured-log"));
        var expectedPrefix = Convert.ToBase64String(
            payload.AsSpan(0, KafkaConsumeFailureRecorder.FallbackLogPayloadCapBytes));
        StringAssert.Contains(fallback.Message, expectedPrefix + KafkaConsumeFailureRecorder.TruncationMarker,
            "over-cap payloads must be truncated at the cap AND flagged with an explicit marker");
        Assert.IsFalse(fallback.Message.Contains(Convert.ToBase64String(payload)),
            "the full payload must not leak into the log");
    }

    [TestMethod]
    public void Record_DlqAndLoggerBothThrow_IsSwallowed()
    {
        // The super-degraded contract: even when IKafkaErrorLog
        // AND the logger throw, Record must not propagate — one missed log
        // entry beats a crashed consume loop.
        var errorLog = new ThrowingErrorLog();
        var logger = new ThrowingLogger();
        var sut = new KafkaConsumeFailureRecorder(errorLog, logger);

        sut.Record(NewEntry(), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.IsTrue(errorLog.WasCalled, "tier 1 (DLQ) must have been attempted");
        Assert.IsTrue(logger.LogErrorAttempted, "tier 2 (structured log) must have been attempted");
    }

    [TestMethod]
    public void Record_InsertTokenIsLinkedToStoppingToken()
    {
        // Shutdown must not be blocked for up to the insert timeout per
        // record: the token handed to InsertAsync must observe the host's
        // stopping token, not just the timeout.
        var errorLog = new RecordingErrorLog();
        var logger = new CapturingLogger();
        var sut = new KafkaConsumeFailureRecorder(errorLog, logger);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        sut.Record(NewEntry(), TimeSpan.FromSeconds(30), stopping.Token);

        Assert.IsTrue(errorLog.LastToken.IsCancellationRequested,
            "the insert token must be linked to the stopping token (already-cancelled stop ⇒ already-cancelled insert token)");
    }

    [TestMethod]
    public void Record_InsertTimeoutCancelsTheInsertToken()
    {
        var errorLog = new BlockUntilCancelledErrorLog();
        var logger = new CapturingLogger();
        var sut = new KafkaConsumeFailureRecorder(errorLog, logger);

        // Never-cancelled stopping token; the 50 ms timeout must free the call.
        sut.Record(NewEntry(), TimeSpan.FromMilliseconds(50), CancellationToken.None);

        // The blocked insert throws OperationCanceledException on timeout,
        // which routes to the structured-log fallback — proving the timeout
        // fired through the linked token.
        Assert.IsTrue(logger.Entries.Any(e => e.Message.Contains("error-fallback-structured-log")));
    }

    [TestMethod]
    public void SerializeTypedPayload_RoundTripsTypedValueAsJson()
    {
        var bytes = KafkaConsumeFailureRecorder.SerializeTypedPayload(new { RequestId = 42, Status = "Failed" });

        Assert.IsNotNull(bytes);
        var json = System.Text.Encoding.UTF8.GetString(bytes!);
        StringAssert.Contains(json, "42");
        StringAssert.Contains(json, "Failed");
    }

    [TestMethod]
    public void SerializeTypedPayload_SerializationFailure_ReturnsNullNotThrows()
    {
        var bytes = KafkaConsumeFailureRecorder.SerializeTypedPayload(new ThrowsOnSerialize());

        Assert.IsNull(bytes);
    }

    private static KafkaErrorLogEntry NewEntry(byte[]? rawPayload = null, string? exceptionType = null) => new()
    {
        Topic = "t",
        Partition = 0,
        Offset = 1,
        ConsumerGroup = "g",
        MessageKey = "k",
        RawPayload = rawPayload,
        Error = "boom",
        ExceptionType = exceptionType,
        OccurredAt = DateTimeOffset.UtcNow
    };

    private sealed class ThrowsOnSerialize
    {
        public string Boom => throw new InvalidOperationException("getter throws");
    }

    private sealed class RecordingErrorLog : IKafkaErrorLog
    {
        public KafkaErrorLogEntry? LastEntry { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
        {
            LastEntry = entry;
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingErrorLog : IKafkaErrorLog
    {
        public bool WasCalled { get; private set; }

        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("DLQ down");
        }
    }

    private sealed class BlockUntilCancelledErrorLog : IKafkaErrorLog
    {
        public async Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class ThrowingLogger : ILogger
    {
        public bool LogErrorAttempted { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                LogErrorAttempted = true;
                throw new InvalidOperationException("Log sink down");
            }
        }
    }
}
