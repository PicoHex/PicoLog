namespace PicoLog.Tests;

public sealed class LoggerExtensionsTests
{
    [Test]
    public async Task SyncExtensions_ForwardExpectedLevels_And_Exceptions()
    {
        var logger = new NativeStructuredRecordingLogger();
        var exception = new InvalidOperationException("sync-failure");

        logger.Trace("trace");
        logger.Debug("debug");
        logger.Info("info");
        logger.Notice("notice", exception);
        logger.Warning("warning", exception);
        logger.Error("error", exception);
        logger.Critical("critical", exception);
        logger.Alert("alert", exception);
        logger.Emergency("emergency", exception);

        await Assert.That(logger.SyncEntries.Count).IsEqualTo(9);
        await Assert
            .That(logger.SyncEntries.Select(entry => entry.Level).ToArray())
            .IsEquivalentTo(
                [
                    LogLevel.Trace,
                    LogLevel.Debug,
                    LogLevel.Info,
                    LogLevel.Notice,
                    LogLevel.Warning,
                    LogLevel.Error,
                    LogLevel.Critical,
                    LogLevel.Alert,
                    LogLevel.Emergency
                ]
            );
        await Assert
            .That(logger.SyncEntries.Select(entry => entry.Message).ToArray())
            .IsEquivalentTo(
                [
                    "trace",
                    "debug",
                    "info",
                    "notice",
                    "warning",
                    "error",
                    "critical",
                    "alert",
                    "emergency"
                ]
            );
        await Assert.That(logger.SyncEntries[0].Exception is null).IsTrue();
        await Assert.That(logger.SyncEntries[1].Exception is null).IsTrue();
        await Assert.That(logger.SyncEntries[2].Exception is null).IsTrue();

        for (var index = 3; index < logger.SyncEntries.Count; index++)
            await Assert.That(logger.SyncEntries[index].Exception).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task AsyncExtensions_ForwardExpectedLevels_Exceptions_And_CancellationToken()
    {
        var logger = new NativeStructuredRecordingLogger();
        var exception = new InvalidOperationException("async-failure");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        await logger.TraceAsync("trace", cancellationToken);
        await logger.DebugAsync("debug", cancellationToken);
        await logger.InfoAsync("info", cancellationToken);
        await logger.NoticeAsync("notice", exception, cancellationToken);
        await logger.WarningAsync("warning", exception, cancellationToken);
        await logger.ErrorAsync("error", exception, cancellationToken);
        await logger.CriticalAsync("critical", exception, cancellationToken);
        await logger.AlertAsync("alert", exception, cancellationToken);
        await logger.EmergencyAsync("emergency", exception, cancellationToken);

        await Assert.That(logger.AsyncEntries.Count).IsEqualTo(9);
        await Assert
            .That(logger.AsyncEntries.Select(entry => entry.Level).ToArray())
            .IsEquivalentTo(
                [
                    LogLevel.Trace,
                    LogLevel.Debug,
                    LogLevel.Info,
                    LogLevel.Notice,
                    LogLevel.Warning,
                    LogLevel.Error,
                    LogLevel.Critical,
                    LogLevel.Alert,
                    LogLevel.Emergency
                ]
            );
        await Assert
            .That(logger.AsyncEntries.Select(entry => entry.Message).ToArray())
            .IsEquivalentTo(
                [
                    "trace",
                    "debug",
                    "info",
                    "notice",
                    "warning",
                    "error",
                    "critical",
                    "alert",
                    "emergency"
                ]
            );
        await Assert
            .That(logger.AsyncEntries.All(entry => entry.CancellationToken == cancellationToken))
            .IsTrue();
        await Assert.That(logger.AsyncEntries[0].Exception is null).IsTrue();
        await Assert.That(logger.AsyncEntries[1].Exception is null).IsTrue();
        await Assert.That(logger.AsyncEntries[2].Exception is null).IsTrue();

        for (var index = 3; index < logger.AsyncEntries.Count; index++)
            await Assert.That(logger.AsyncEntries[index].Exception).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task StructuredExtensions_DelegateToNativeILoggerPropertyOverloads_WhenAvailable()
    {
        ILogger logger = new NativeStructuredRecordingLogger();
        var exception = new InvalidOperationException("structured-failure");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        IReadOnlyList<KeyValuePair<string, object?>> properties =
        [
            new("tenant", "alpha"),
            new("attempt", 3)
        ];

        logger.LogStructured(LogLevel.Warning, "sync-structured", properties, exception);
        await logger.LogStructuredAsync(
            LogLevel.Error,
            "async-structured",
            properties,
            exception,
            cancellationToken
        );

        var recordingLogger = (NativeStructuredRecordingLogger)logger;

        await Assert.That(recordingLogger.SyncStructuredEntries.Count).IsEqualTo(1);
        await Assert.That(recordingLogger.AsyncStructuredEntries.Count).IsEqualTo(1);
        await Assert.That(recordingLogger.SyncStructuredEntries[0].Properties[0].Key).IsEqualTo("tenant");
        await Assert.That(recordingLogger.SyncStructuredEntries[0].Properties[0].Value).IsEqualTo("alpha");
        await Assert.That(recordingLogger.AsyncStructuredEntries[0].Properties[1].Key).IsEqualTo("attempt");
        await Assert.That(recordingLogger.AsyncStructuredEntries[0].Properties[1].Value).IsEqualTo(3);
        await Assert.That(recordingLogger.AsyncStructuredEntries[0].CancellationToken == cancellationToken).IsTrue();
        await Assert.That(recordingLogger.SyncEntries.Count).IsEqualTo(0);
        await Assert.That(recordingLogger.AsyncEntries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task StructuredExtensions_OnLegacyILogger_FallBackToDefaultInterfaceMethods_And_DropStructuredProperties()
    {
        ILogger logger = new LegacyRecordingLogger();
        var exception = new InvalidOperationException("plain-failure");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        IReadOnlyList<KeyValuePair<string, object?>> properties = [new("tenant", "alpha")];

        logger.LogStructured(LogLevel.Warning, "sync-fallback", properties, exception);
        await logger.LogStructuredAsync(
            LogLevel.Error,
            "async-fallback",
            properties,
            exception,
            cancellationToken
        );

        var legacyLogger = (LegacyRecordingLogger)logger;

        await Assert.That(legacyLogger.SyncEntries.Count).IsEqualTo(1);
        await Assert.That(legacyLogger.AsyncEntries.Count).IsEqualTo(1);
        await Assert.That(legacyLogger.SyncEntries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(legacyLogger.SyncEntries[0].Message).IsEqualTo("sync-fallback");
        await Assert.That(legacyLogger.SyncEntries[0].Exception).IsSameReferenceAs(exception);
        await Assert.That(legacyLogger.AsyncEntries[0].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(legacyLogger.AsyncEntries[0].Message).IsEqualTo("async-fallback");
        await Assert.That(legacyLogger.AsyncEntries[0].Exception).IsSameReferenceAs(exception);
        await Assert.That(legacyLogger.AsyncEntries[0].CancellationToken == cancellationToken).IsTrue();
    }

    [Test]
    public async Task TypedLogger_WithLegacyInnerLogger_UsesDefaultInterfaceFallback_And_DropsProperties()
    {
        var innerLogger = new LegacyRecordingLogger();
        var logger = new Logger<TypedCategory>(new StubLoggerFactory(innerLogger));
        var exception = new InvalidOperationException("typed-plain-failure");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        IReadOnlyList<KeyValuePair<string, object?>> properties = [new("tenant", "alpha")];

        logger.LogStructured(LogLevel.Warning, "typed-sync-fallback", properties, exception);
        await logger.LogStructuredAsync(
            LogLevel.Error,
            "typed-async-fallback",
            properties,
            exception,
            cancellationToken
        );

        await Assert.That(innerLogger.SyncEntries.Count).IsEqualTo(1);
        await Assert.That(innerLogger.AsyncEntries.Count).IsEqualTo(1);
        await Assert.That(innerLogger.SyncEntries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(innerLogger.SyncEntries[0].Message).IsEqualTo("typed-sync-fallback");
        await Assert.That(innerLogger.SyncEntries[0].Exception).IsSameReferenceAs(exception);
        await Assert.That(innerLogger.AsyncEntries[0].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(innerLogger.AsyncEntries[0].Message).IsEqualTo("typed-async-fallback");
        await Assert.That(innerLogger.AsyncEntries[0].Exception).IsSameReferenceAs(exception);
        await Assert.That(innerLogger.AsyncEntries[0].CancellationToken == cancellationToken).IsTrue();
    }

    private sealed class NativeStructuredRecordingLogger : ILogger
    {
        public List<RecordedEntry> SyncEntries { get; } = [];
        public List<RecordedEntry> AsyncEntries { get; } = [];
        public List<StructuredRecordedEntry> SyncStructuredEntries { get; } = [];
        public List<StructuredRecordedEntry> AsyncStructuredEntries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public void Log(LogLevel logLevel, string message, Exception? exception = null)
        {
            SyncEntries.Add(new RecordedEntry(logLevel, message, exception, default));
        }

        public void Log(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception
        )
        {
            SyncStructuredEntries.Add(
                new StructuredRecordedEntry(logLevel, message, properties ?? [], exception, default)
            );
        }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        )
        {
            AsyncEntries.Add(new RecordedEntry(logLevel, message, exception, cancellationToken));
            return Task.CompletedTask;
        }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception,
            CancellationToken cancellationToken = default
        )
        {
            AsyncStructuredEntries.Add(
                new StructuredRecordedEntry(
                    logLevel,
                    message,
                    properties ?? [],
                    exception,
                    cancellationToken
                )
            );
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyRecordingLogger : ILogger
    {
        public List<RecordedEntry> SyncEntries { get; } = [];
        public List<RecordedEntry> AsyncEntries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public void Log(LogLevel logLevel, string message, Exception? exception = null)
        {
            SyncEntries.Add(new RecordedEntry(logLevel, message, exception, default));
        }

        public void Log(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception
        )
        {
            SyncEntries.Add(new RecordedEntry(logLevel, message, exception, default));
        }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        )
        {
            AsyncEntries.Add(new RecordedEntry(logLevel, message, exception, cancellationToken));
            return Task.CompletedTask;
        }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception,
            CancellationToken cancellationToken = default
        )
        {
            AsyncEntries.Add(new RecordedEntry(logLevel, message, exception, cancellationToken));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        CancellationToken CancellationToken
    );

    private sealed record StructuredRecordedEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties,
        Exception? Exception,
        CancellationToken CancellationToken
    );

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose() { }
    }

    private sealed class StubLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TypedCategory;
}
